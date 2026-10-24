import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, RequestListItem, RequestQueryParams, RequestSummary } from '../models/request.model';

@Injectable({ providedIn: 'root' })
export class RequestsApiService {
  private readonly http = inject(HttpClient);

  getRequests(params: RequestQueryParams): Observable<PagedResult<RequestListItem>> {
    // Build query string only for defined, non-empty fields.
    // HttpParams is immutable — each .set() returns a new instance.
    let p = new HttpParams()
      .set('page', params.page.toString())
      .set('pageSize', params.pageSize.toString());

    if (params.status)           p = p.set('status',           params.status);
    if (params.priority)         p = p.set('priority',         params.priority);
    if (params.organizationName) p = p.set('organizationName', params.organizationName);
    if (params.assignedTo)       p = p.set('assignedTo',       params.assignedTo);
    if (params.createdFrom)      p = p.set('createdFrom',      params.createdFrom);
    if (params.createdTo)        p = p.set('createdTo',        params.createdTo);
    if (params.search)           p = p.set('search',           params.search);
    if (params.sortBy)           p = p.set('sortBy',           params.sortBy);
    if (params.sortDirection)    p = p.set('sortDirection',    params.sortDirection);

    return this.http.get<PagedResult<RequestListItem>>('/api/requests', { params: p });
  }

  getSummary(): Observable<RequestSummary> {
    return this.http.get<RequestSummary>('/api/requests/summary');
  }
}
