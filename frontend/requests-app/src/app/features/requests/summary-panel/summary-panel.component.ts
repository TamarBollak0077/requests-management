import { Component, DestroyRef, Input, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Observable, Subject, EMPTY, merge, of, switchMap, map, catchError, startWith } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RequestsApiService } from '../../../core/services/requests-api.service';
import { RequestSummary, RequestStatus, RequestPriority, StatusCount, PriorityCount } from '../../../core/models/request.model';

type SummaryState =
  | { kind: 'loading' }
  | { kind: 'loaded'; summary: NormalizedSummary }
  | { kind: 'error' };

interface NormalizedSummary {
  byStatus: StatusCount[];
  byPriority: PriorityCount[];
}

const ALL_STATUSES: RequestStatus[] = ['New', 'InProgress', 'Waiting', 'Completed'];
const ALL_PRIORITIES: RequestPriority[] = ['Low', 'Medium', 'High'];

@Component({
  selector: 'app-summary-panel',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './summary-panel.component.html',
  styleUrl: './summary-panel.component.css',
})
export class SummaryPanelComponent implements OnInit {
  private readonly api = inject(RequestsApiService);
  private readonly destroyRef = inject(DestroyRef);

  /** Emit on this observable to trigger a refresh (used by future status-update feature). */
  @Input() refreshTrigger$?: Observable<void>;

  state: SummaryState = { kind: 'loading' };

  private readonly retry$ = new Subject<void>();

  ngOnInit(): void {
    const trigger$ = merge(
      of(undefined as void),
      this.refreshTrigger$ ?? EMPTY,
      this.retry$,
    );

    trigger$
      .pipe(
        switchMap(() =>
          this.api.getSummary().pipe(
            map((s): SummaryState => ({
              kind: 'loaded',
              summary: {
                byStatus: this.fillStatuses(s.byStatus),
                byPriority: this.fillPriorities(s.byPriority),
              },
            })),
            catchError(() => of<SummaryState>({ kind: 'error' })),
            startWith<SummaryState>({ kind: 'loading' }),
          )
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((s) => (this.state = s));
  }

  retry(): void {
    this.retry$.next();
  }

  statusClass(status: string): string {
    return `status-${status.toLowerCase()}`;
  }

  priorityClass(priority: string): string {
    return `priority-${priority.toLowerCase()}`;
  }

  private fillStatuses(items: StatusCount[]): StatusCount[] {
    const map = new Map(items.map((i) => [i.status, i.count]));
    return ALL_STATUSES.map((s) => ({ status: s, count: map.get(s) ?? 0 }));
  }

  private fillPriorities(items: PriorityCount[]): PriorityCount[] {
    const map = new Map(items.map((i) => [i.priority, i.count]));
    return ALL_PRIORITIES.map((p) => ({ priority: p, count: map.get(p) ?? 0 }));
  }
}
