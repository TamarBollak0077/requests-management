import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Subject, switchMap, catchError, of, startWith, map, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RequestsApiService } from '../../core/services/requests-api.service';
import {
  PagedResult, RequestListItem, RequestQueryParams,
  RequestStatus, RequestPriority, SortBy, SortDirection,
  RequestStatusHistoryItem,
} from '../../core/models/request.model';
import { SummaryPanelComponent } from './summary-panel/summary-panel.component';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: PagedResult<RequestListItem> }
  | { kind: 'empty' }
  | { kind: 'error'; message: string };

interface FilterState {
  status: RequestStatus | '';
  priority: RequestPriority | '';
  organizationName: string;
  assignedTo: string;
  createdFrom: string;
  createdTo: string;
  search: string;
  sortBy: SortBy;
  sortDirection: SortDirection;
}

interface RowUiState {
  updating: boolean;
  error: string | null;
}

interface RowPatch {
  status: RequestStatus;
  rowVersion: string;
}

type HistoryState =
  | { kind: 'loading' }
  | { kind: 'loaded'; items: RequestStatusHistoryItem[] }
  | { kind: 'empty' }
  | { kind: 'error' };

const DEFAULT_FILTERS: FilterState = {
  status: '',
  priority: '',
  organizationName: '',
  assignedTo: '',
  createdFrom: '',
  createdTo: '',
  search: '',
  sortBy: 'createdAt',
  sortDirection: 'desc',
};

// Mirrors backend StatusTransitionPolicy — kept in sync manually.
const ALLOWED_TRANSITIONS: Record<RequestStatus, RequestStatus[]> = {
  New:        ['InProgress', 'Waiting'],
  InProgress: ['Waiting',    'Completed'],
  Waiting:    ['InProgress', 'Completed'],
  Completed:  [],
};

@Component({
  selector: 'app-requests-page',
  standalone: true,
  imports: [CommonModule, FormsModule, SummaryPanelComponent],
  templateUrl: './requests-page.component.html',
  styleUrl: './requests-page.component.css',
})
export class RequestsPageComponent {
  private readonly api = inject(RequestsApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly pageSize = 20;

  /**
   * Emit here after a successful status update to refresh the summary panel.
   * T-703 wires this into <app-summary-panel [refreshTrigger$]="summaryRefresh$">.
   */
  readonly summaryRefresh$ = new Subject<void>();

  // ── Filter form state ────────────────────────────────────────────────
  filters: FilterState = { ...DEFAULT_FILTERS };

  readonly statuses: RequestStatus[] = ['New', 'InProgress', 'Waiting', 'Completed'];
  readonly priorities: RequestPriority[] = ['Low', 'Medium', 'High'];
  readonly sortFields: { value: SortBy; label: string }[] = [
    { value: 'createdAt', label: 'Created date' },
    { value: 'priority', label: 'Priority' },
    { value: 'title', label: 'Title' },
  ];

  // ── Row-level optimistic update state ───────────────────────────────
  // Keyed by request id.  Plain objects (not Map) for Angular CD compatibility.

  /** Successful-update overrides for status/rowVersion pending the next page reload. */
  rowPatches: Record<number, RowPatch> = {};

  /** Per-row UI state: spinner and error message. */
  rowState: Record<number, RowUiState> = {};

  // ── Row-level history state ──────────────────────────────────────────
  /** Whether the history panel is expanded for a given row id. */
  historyOpen: Record<number, boolean> = {};
  /** Cached history fetch state per row id. */
  historyState: Record<number, HistoryState> = {};

  // ── RxJS pipeline ────────────────────────────────────────────────────

  private readonly params$ = new BehaviorSubject<RequestQueryParams>(
    this.buildParams(1)
  );

  private readonly textChange$ = new Subject<void>();

  readonly state$ = this.params$.pipe(
    switchMap((params) =>
      this.api.getRequests(params).pipe(
        map((result): LoadState =>
          result.totalCount === 0
            ? { kind: 'empty' }
            : { kind: 'loaded', data: result }
        ),
        catchError((err) => {
          console.error('Failed to load requests', err);
          return of<LoadState>({
            kind: 'error',
            message: 'Could not load requests. Please try again.',
          });
        }),
        startWith<LoadState>({ kind: 'loading' })
      )
    )
  );

  constructor() {
    this.textChange$
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe(() => this.params$.next(this.buildParams(1)));

    // Clear per-row overrides whenever a new page query fires so fresh
    // server data always wins after a filter/sort/pagination change.
    this.params$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.rowPatches = {};
      this.rowState = {};
      this.historyOpen = {};
      this.historyState = {};
    });
  }

  // ── Filter event handlers ────────────────────────────────────────────

  onTextChange(): void { this.textChange$.next(); }
  onImmediateChange(): void { this.params$.next(this.buildParams(1)); }

  toggleSortDirection(): void {
    this.filters.sortDirection = this.filters.sortDirection === 'desc' ? 'asc' : 'desc';
    this.params$.next(this.buildParams(1));
  }

  resetFilters(): void {
    this.filters = { ...DEFAULT_FILTERS };
    this.params$.next(this.buildParams(1));
  }

  retry(): void { this.params$.next({ ...this.params$.value }); }

  goToPage(page: number): void { this.params$.next(this.buildParams(page)); }

  // ── Status update ────────────────────────────────────────────────────

  /** Effective status for a row: patched value if available, else the server value. */
  getEffectiveStatus(req: RequestListItem): RequestStatus {
    return this.rowPatches[req.id]?.status ?? req.status;
  }

  /** Effective rowVersion for a row: patched value if available, else the server value. */
  getEffectiveRowVersion(req: RequestListItem): string {
    return this.rowPatches[req.id]?.rowVersion ?? req.rowVersion;
  }

  /** Per-row UI state; returns a safe default when no entry exists. */
  getRowState(id: number): RowUiState {
    return this.rowState[id] ?? { updating: false, error: null };
  }

  /** Valid next statuses for a given current status. */
  allowedTransitions(status: RequestStatus): RequestStatus[] {
    return ALLOWED_TRANSITIONS[status] ?? [];
  }

  /**
   * Called when the user picks a new status from a row's select.
   * Sends PATCH /api/requests/{id}/status and handles 200 / 400 / 409.
   */
  onStatusChange(req: RequestListItem, newStatus: RequestStatus): void {
    const id = req.id;
    const rowVersion = this.getEffectiveRowVersion(req);

    this.rowState[id] = { updating: true, error: null };

    this.api.updateStatus(id, { newStatus, rowVersion, changedBy: 'user' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.rowPatches[id] = { status: result.status, rowVersion: result.rowVersion };
          this.rowState[id] = { updating: false, error: null };
          this.summaryRefresh$.next();
          // Invalidate the cached history so the new audit entry is visible.
          // If the panel is open, reload immediately; otherwise clear so the
          // next open fetches fresh data.
          delete this.historyState[id];
          if (this.historyOpen[id]) {
            this.loadHistory(id);
          }
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 409) {
            // 409 body is ProblemDetails; currentStatus + currentRowVersion are
            // top-level extension properties serialized by ASP.NET Core.
            const body = err.error as {
              currentStatus?: string;
              currentRowVersion?: string;
            };
            const currentStatus = (body.currentStatus as RequestStatus | undefined)
              ?? this.getEffectiveStatus(req);
            const currentRowVersion = body.currentRowVersion ?? rowVersion;
            this.rowPatches[id] = { status: currentStatus, rowVersion: currentRowVersion };
            this.rowState[id] = {
              updating: false,
              error: 'הפנייה עודכנה על ידי משתמש אחר',
            };
          } else if (err.status === 400) {
            const detail: string =
              err.error?.detail ?? err.error?.title ?? 'Invalid status transition.';
            this.rowState[id] = { updating: false, error: detail };
          } else {
            this.rowState[id] = {
              updating: false,
              error: 'Update failed. Please try again.',
            };
          }
        },
      });
  }

  // ── History ──────────────────────────────────────────────────────────

  /**
   * Toggle the history panel for a row.
   * On first open: fetches from the API. On subsequent opens: uses cached data.
   */
  toggleHistory(id: number): void {
    if (this.historyOpen[id]) {
      this.historyOpen[id] = false;
      return;
    }
    this.historyOpen[id] = true;
    const existing = this.historyState[id];
    if (existing?.kind === 'loaded' || existing?.kind === 'empty') return;
    this.loadHistory(id);
  }

  loadHistory(id: number): void {
    this.historyState[id] = { kind: 'loading' };
    this.api.getHistory(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.historyState[id] = items.length === 0
            ? { kind: 'empty' }
            : { kind: 'loaded', items };
        },
        error: () => {
          this.historyState[id] = { kind: 'error' };
        },
      });
  }

  getHistoryState(id: number): HistoryState {
    return this.historyState[id] ?? { kind: 'loading' };
  }

  getHistoryItems(id: number): RequestStatusHistoryItem[] {
    const s = this.historyState[id];
    return s?.kind === 'loaded' ? s.items : [];
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  get currentPage(): number { return this.params$.value.page; }

  get activeFilterCount(): number {
    const f = this.filters;
    return [
      f.status, f.priority, f.organizationName.trim(),
      f.assignedTo.trim(), f.createdFrom, f.createdTo, f.search.trim(),
    ].filter(Boolean).length;
  }

  pageNumbers(totalPages: number): number[] {
    const current = this.currentPage;
    const delta = 2;
    const start = Math.max(1, current - delta);
    const end = Math.min(totalPages, current + delta);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  }

  priorityClass(priority: string): string { return `priority-${priority.toLowerCase()}`; }
  statusClass(status: string): string { return `status-${status.toLowerCase()}`; }

  formatDate(isoString: string): string {
    return new Date(isoString).toLocaleDateString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric',
    });
  }

  formatDateTime(isoString: string): string {
    return new Date(isoString).toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  }

  private buildParams(page: number): RequestQueryParams {
    const f = this.filters;
    return {
      page,
      pageSize: this.pageSize,
      ...(f.status && { status: f.status }),
      ...(f.priority && { priority: f.priority }),
      ...(f.organizationName.trim() && { organizationName: f.organizationName.trim() }),
      ...(f.assignedTo.trim() && { assignedTo: f.assignedTo.trim() }),
      ...(f.createdFrom && { createdFrom: f.createdFrom }),
      ...(f.createdTo && { createdTo: f.createdTo }),
      ...(f.search.trim() && { search: f.search.trim() }),
      sortBy: f.sortBy,
      sortDirection: f.sortDirection,
    };
  }
}
