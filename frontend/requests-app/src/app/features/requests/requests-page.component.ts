import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BehaviorSubject, switchMap, catchError, of, startWith, map } from 'rxjs';
import { RequestsApiService } from '../../core/services/requests-api.service';
import { PagedResult, RequestListItem, RequestQueryParams } from '../../core/models/request.model';

// Discriminated union representing the four possible UI states.
// Using a union (not booleans) makes templates exhaustive and prevents
// impossible combinations such as loading=true and error=true simultaneously.
type LoadState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: PagedResult<RequestListItem> }
  | { kind: 'empty' }
  | { kind: 'error'; message: string };

@Component({
  selector: 'app-requests-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './requests-page.component.html',
  styleUrl: './requests-page.component.css',
})
export class RequestsPageComponent implements OnInit {
  private readonly api = inject(RequestsApiService);

  readonly pageSize = 20;

  // BehaviorSubject holds the current query params.  Pushing a new value
  // triggers the switchMap below, which cancels any in-flight HTTP request
  // before starting the new one — preventing stale responses from landing.
  private readonly params$ = new BehaviorSubject<RequestQueryParams>({
    page: 1,
    pageSize: this.pageSize,
  });

  // The observable that drives the template.
  // switchMap: when params$ emits, cancel the previous HTTP request and start a new one.
  // startWith: emit 'loading' immediately so the spinner shows before the first response.
  // catchError inside the pipe (not on the outer observable) keeps the stream alive
  // so later page changes still work after an error.
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

  get currentPage(): number {
    return this.params$.value.page;
  }

  ngOnInit(): void {
    // params$ already has an initial value — the first HTTP call fires automatically.
  }

  goToPage(page: number): void {
    this.params$.next({ ...this.params$.value, page });
  }

  retry(): void {
    // Re-emit the current params to retry the request.
    this.params$.next({ ...this.params$.value });
  }

  // Helper for the template: generate an array of page numbers to show in the pager.
  pageNumbers(totalPages: number): number[] {
    const current = this.currentPage;
    const delta = 2;
    const start = Math.max(1, current - delta);
    const end = Math.min(totalPages, current + delta);
    return Array.from({ length: end - start + 1 }, (_, i) => start + i);
  }

  // Priority label with a CSS-friendly class name.
  priorityClass(priority: string): string {
    return `priority-${priority.toLowerCase()}`;
  }

  statusClass(status: string): string {
    return `status-${status.toLowerCase()}`;
  }

  formatDate(isoString: string): string {
    return new Date(isoString).toLocaleDateString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }
}
