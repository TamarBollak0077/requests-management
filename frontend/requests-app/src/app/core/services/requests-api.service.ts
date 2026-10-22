import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PagedResult, RequestListItem, RequestQueryParams } from '../models/request.model';

@Injectable({ providedIn: 'root' })
export class RequestsApiService {
  private readonly http = inject(HttpClient);

  getRequests(params: RequestQueryParams): Observable<PagedResult<RequestListItem>> {
    const httpParams = new HttpParams()
      .set('page', params.page.toString())
      .set('pageSize', params.pageSize.toString());

    return this.http.get<PagedResult<RequestListItem>>('/api/requests', { params: httpParams });
  }
}
