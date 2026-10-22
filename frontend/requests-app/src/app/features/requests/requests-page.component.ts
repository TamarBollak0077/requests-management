import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, Subject, switchMap, catchError, of, startWith, map, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RequestsApiService } from '../../core/services/requests-api.service';
import { PagedResult, RequestListItem, RequestQueryParams, RequestStatus, RequestPriority, SortBy, SortDirection } from '../../core/models/request.model';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: PagedResult<RequestListItem> }
  | { kind: 'empty' }
  | { kind: 'error'; message: string };

// All filter/sort fields as a plain object — separate from pagination.
// This is the "form state"; params$ is the derived "query state".
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

@Component({
  selector: 'app-requests-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './requests-page.component.html',
  styleUrl: './requests-page.component.css',
})
export class RequestsPageComponent {
  private readonly api = inject(RequestsApiService);

  readonly pageSize = 20;

  // ── Filter form state ────────────────────────────────────────────────
  // Mutable object bound via [(ngModel)].  Not directly in params$ so
  // text fields can debounce before the HTTP call fires.
  filters: FilterState = { ...DEFAULT_FILTERS };

  // Exposed for the template select/option lists
  readonly statuses: RequestStatus[] = ['New', 'InProgress', 'Waiting', 'Completed'];
  readonly priorities: RequestPriority[] = ['Low', 'Medium', 'High'];
  readonly sortFields: { value: SortBy; label: string }[] = [
    { value: 'createdAt', label: 'Created date' },
    { value: 'priority', label: 'Priority' },
    { value: 'title', label: 'Title' },
  ];

  // ── RxJS pipeline ────────────────────────────────────────────────────

  // params$ is the single source of truth for what's being fetched.
  // Every push here triggers a new (cancellable) HTTP call via switchMap.
  private readonly params$ = new BehaviorSubject<RequestQueryParams>(
    this.buildParams(1)
  );

  // Text-input changes go through this Subject first, then debounce 300 ms
  // before being forwarded to params$.  Non-text changes bypass this.
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
    // Debounce text-field changes then push to params$.
    // takeUntilDestroyed() — called in injection context (constructor) —
    // auto-injects DestroyRef and unsubscribes when the component is destroyed.
    this.textChange$
      .pipe(
        debounceTime(300),
        takeUntilDestroyed()
      )
      .subscribe(() => this.params$.next(this.buildParams(1)));
  }

  // ── Template event handlers ──────────────────────────────────────────

  /** Called by (ngModelChange) on free-text inputs; deferred via debounce. */
  onTextChange(): void {
    this.textChange$.next();
  }

  /** Called by (change) on select and date inputs; fires immediately. */
  onImmediateChange(): void {
    this.params$.next(this.buildParams(1));
  }

  toggleSortDirection(): void {
    this.filters.sortDirection = this.filters.sortDirection === 'desc' ? 'asc' : 'desc';
    this.params$.next(this.buildParams(1));
  }

  resetFilters(): void {
    this.filters = { ...DEFAULT_FILTERS };
    this.params$.next(this.buildParams(1));
  }

  retry(): void {
    this.params$.next({ ...this.params$.value });
  }

  goToPage(page: number): void {
    // Page changes keep current filters and sort — only reset page.
    this.params$.next(this.buildParams(page));
  }

  // ── Helpers ──────────────────────────────────────────────────────────

  get currentPage(): number {
    return this.params$.value.page;
  }

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
