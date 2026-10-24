export type RequestStatus = 'New' | 'InProgress' | 'Waiting' | 'Completed';
export type RequestPriority = 'Low' | 'Medium' | 'High';

export interface StatusCount { status: RequestStatus; count: number; }
export interface PriorityCount { priority: RequestPriority; count: number; }
export interface RequestSummary { byStatus: StatusCount[]; byPriority: PriorityCount[]; }

export interface RequestStatusHistoryItem {
  requestId: number;
  previousStatus: RequestStatus;
  newStatus: RequestStatus;
  changedAt: string; // ISO 8601 UTC string
  changedBy: string;
}

export interface UpdateStatusRequest {
  newStatus: RequestStatus;
  rowVersion: string; // Base64 — value from the last GET response
  changedBy: string;
}

export interface UpdateStatusResult {
  id: number;
  status: RequestStatus;
  rowVersion: string; // Base64 — new server-generated value; must be used for subsequent updates
  updatedAt: string;
}
export type SortBy = 'createdAt' | 'priority' | 'title';
export type SortDirection = 'asc' | 'desc';

export interface RequestListItem {
  id: number;
  title: string;
  organizationName: string;
  status: RequestStatus;
  priority: RequestPriority;
  assignedTo: string | null;
  createdAt: string; // ISO 8601 UTC string
  updatedAt: string;
  rowVersion: string; // Base64 — needed for future status updates
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface RequestQueryParams {
  page: number;
  pageSize: number;
  // Filters — all optional; undefined means "no filter"
  status?: RequestStatus;
  priority?: RequestPriority;
  organizationName?: string;
  assignedTo?: string;
  createdFrom?: string; // YYYY-MM-DD, inclusive lower bound
  createdTo?: string;   // YYYY-MM-DD, inclusive upper bound
  search?: string;      // free text: Title OR OrganizationName
  // Sort
  sortBy?: SortBy;
  sortDirection?: SortDirection;
}
