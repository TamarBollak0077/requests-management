# Technical Decisions — docs/decisions.md

This document covers the significant design decisions made in this project, the alternatives considered, known limitations, and AI tool usage. It addresses requirements R-11.4 through R-11.9 from the assignment specification.

---

## 1. Database: SQL Server

**Chosen:** SQL Server 2019 / 2022 (full instance; Express edition supported).

### Rationale

| Factor | SQL Server | MongoDB |
|---|---|---|
| Schema | Fixed, well-defined entity with typed fields | Flexible schema — no benefit here |
| Concurrency | Built-in `rowversion` type; EF Core maps it directly via `IsRowVersion()` | No native row-version; requires a manual `int version` field + `findOneAndUpdate` with condition |
| Transactionality | `UPDATE Requests + INSERT RequestStatusHistory` in a single implicit EF transaction | Supported but more complex to coordinate across documents |
| Aggregations | Native `GROUP BY` with index seek | `$group` pipeline; straightforward but an extra abstraction layer |
| Tooling | EF Core 8 matures on SQL Server; `rowversion` concurrency works out of the box | EF Core MongoDB provider exists but is less mature for concurrency scenarios |
| Environment | SQL Server was already installed locally; no additional setup | Would require a running MongoDB instance |

**Alternatives considered:**

- **MongoDB** — viable for flexible documents, but adds no value for this structured, fixed-schema data model. Concurrency would need a manual version field and conditional update. Audit history could be embedded in the document, but the document would grow unboundedly.
- **SQLite / PostgreSQL** — SQLite does not support `rowversion`; EF Core would require a different concurrency mechanism. PostgreSQL was not installed locally and would have added setup overhead.

### Indexes

Five nonclustered indexes were added in a dedicated migration (`AddIndexes`) after the initial schema migration. Rationale per index:

| Index | Key columns | Covers |
|---|---|---|
| `IX_Requests_CreatedAt` | `CreatedAt DESC` | Date-range-only queries; default sort when no leading filter |
| `IX_Requests_Status_CreatedAt` | `Status ASC, CreatedAt DESC` | Status filter + default sort; `GROUP BY Status` in summary |
| `IX_Requests_Priority_CreatedAt` | `Priority ASC, CreatedAt DESC` | Priority filter; `GROUP BY Priority` in summary |
| `IX_Requests_AssignedTo_CreatedAt` | `AssignedTo ASC, CreatedAt DESC` | "My requests" filter |
| `IX_Requests_OrganizationName` | `OrganizationName ASC` | `StartsWith` prefix filter (sargable); `LIKE '%term%'` is not covered |

`Status` and `Priority` are stored as `tinyint` (`HasConversion<byte>()`) to keep index leaf rows narrow. `Id` is an `int IDENTITY` clustered index (narrow, sequential, default).

Measured impact: see [docs/performance.md](performance.md).

---

## 2. Query Efficiency

**Decision: Build the full query as `IQueryable<T>` before any materialization.**

### How it works (verified in `RequestQueryService.cs`)

1. `db.Requests.AsNoTracking()` — starts an unexecuted `IQueryable`; skips EF's identity-map tracking since this is a read-only path.
2. Each optional filter appends a `.Where(...)` predicate only when the parameter is non-null or non-empty. No predicate = no SQL `WHERE` clause for that column.
3. `CountAsync(ct)` executes `SELECT COUNT(*)` with all active filters. EF strips `ORDER BY` for count queries.
4. `ApplySort(...)` appends `ORDER BY` using a whitelist switch (`createdAt`, `priority`, `title`) plus `Id` as a deterministic tiebreaker for stable pagination.
5. `.Skip(...).Take(...).Select(r => new RequestListItemDto(...))` runs as a single SQL statement: `OFFSET n ROWS FETCH NEXT m ROWS ONLY`, projecting only the required columns. No full entity is materialized; no `ToList()` occurs before pagination.

**`RowVersion` note:** `byte[]` cannot be Base64-encoded inside SQL, so the projection fetches it raw and converts in C# inside the `.Select()` lambda.

### Free-text search and optimizer behavior

The `Search` predicate is positioned last in the LINQ chain. SQL Server's query optimizer determines the actual predicate evaluation order and execution plan based on statistics and estimated costs; it may reorder predicates independently of their order in the LINQ expression. In practice, when other sargable filters are active (e.g., a `Status` index seek), the optimizer tends to reduce the working set before applying the non-sargable scan, but this is not guaranteed by LINQ ordering alone. The positioning is a low-cost mitigation, not a strict execution guarantee.

---

## 3. Optimistic Concurrency

**Decision: SQL Server `rowversion` column, enforced in the database `WHERE` clause.**

### Mechanism (verified in `RequestStatusService.cs`)

1. The client receives `rowVersion` (Base64) in every `GET /api/requests/{id}` or list response.
2. On `PATCH /api/requests/{id}/status`, the client sends the token back in the request body.
3. The service calls `db.Entry(request).Property(r => r.RowVersion).OriginalValue = clientRowVersion`, which makes EF emit:
   ```sql
   UPDATE [Requests] SET [Status]=@s, [UpdatedAt]=@t
   WHERE [Id]=@id AND [RowVersion]=@clientToken
   ```
4. If another writer changed the row after the client read it, 0 rows are affected and EF throws `DbUpdateConcurrencyException`. The service catches it, reads the current DB values, and returns HTTP 409 with `currentStatus` and `currentRowVersion` in the response body so the UI can refresh and retry.

### Audit consistency

The status update and the `RequestStatusHistory` insert are added to the same `DbContext` before the single `SaveChangesAsync` call. EF wraps them in one implicit transaction. If the `UPDATE` is rolled back (concurrency conflict), the history row is never inserted — there is no partial audit.

### Alternatives considered

- **Pessimistic locking (`UPDLOCK`)** — holds a database lock for the duration between the user reading and submitting, which is seconds or minutes in a UI interaction. Inappropriate for HTTP.
- **`int` version column** — works but requires application code to increment it; `rowversion` is incremented automatically by SQL Server on every `UPDATE`, which is simpler and less error-prone.
- **ETag / `If-Match` header** — a cleaner REST pattern, but for the Bulk endpoint each item needs its own version in the request body anyway. Choosing a single approach (version in body) keeps the API contract uniform.

---

## 4. Bulk Status Update — Partial Success

**Decision: each item is saved in its own transaction; partial success is returned.**

### Design (verified in `RequestStatusService.BulkUpdateStatusAsync`)

1. All requested IDs are loaded in a single `WHERE Id IN (...)` query.
2. Items are processed in a `foreach` loop. For each item: validate transition, inject `RowVersion` as the concurrency token, queue the audit row in the same `DbContext`, then call `SaveChangesAsync`. Each call wraps `UPDATE + INSERT RequestStatusHistory` in one implicit transaction.
3. On `DbUpdateConcurrencyException`, the failed `Request` entry and the orphaned `RequestStatusHistory` entry are both detached from the `ChangeTracker` before the next iteration, preventing accidental retry.
4. The response is always HTTP 200 with a `results[]` array. Each item carries an `outcome`: `Updated`, `NotFound`, `Conflict`, or `InvalidTransition`.

**Validation at the request level (HTTP 400, before any DB access):** empty items list, more than 100 items, duplicate IDs, invalid `newStatus`, missing `changedBy`.

### Partial Success vs. All-or-Nothing

| | Partial Success (chosen) | All-or-Nothing |
|---|---|---|
| A conflict on one item | Other items proceed | Entire batch rolled back |
| Client recovery | Retry only failed items | Retry all items |
| Concurrent users | Each update is independently serializable | One conflict fails everyone |
| Implementation complexity | One `SaveChanges` per item (up to 100 round-trips) | One `SaveChanges` for all, requires `ex.Entries` triage |

**Known trade-off:** up to 100 database round-trips. This is documented as a known bottleneck. A batch approach using `ex.Entries` analysis could reduce round-trips, but adds significant complexity.

---

## 5. Caching

**Decision: `IMemoryCache` on the summary endpoint, 60-second absolute expiration.**

### What is cached and why (verified in `RequestQueryService` and `SummaryCacheOptions`)

`GET /api/requests/summary` executes two `GROUP BY` queries over 100,000 rows on every call. The result is identical for all users. Caching it in process memory avoids repeated aggregation under concurrent read load.

List queries (`GET /api/requests`) are not cached because they have hundreds of possible parameter combinations, giving a low expected cache-hit rate.

### Invalidation (verified in `RequestStatusService`)

`cache.Remove(CacheKeys.Summary)` is called after every successful `SaveChangesAsync` — both in the single-item update and in each successful item in the bulk loop. The cache is never invalidated on failures (no status change occurred).

**Race condition:** a `GetOrCreateAsync` call that starts before an invalidation and completes after it will write a stale value back to the cache. The 60-second TTL bounds the exposure window. This is a known, accepted trade-off for an in-process cache.

### Multi-instance limitation

`IMemoryCache` is per-process. In a scaled-out deployment, each instance holds its own cache. An invalidation in instance A is invisible to instance B. At most 60 seconds of stale data per instance.

**Improvement path (not implemented):** Replace with `IDistributedCache` (e.g., Redis) using the same key and invalidation call. Switching the cache interface is only the first step — an appropriate cross-instance invalidation and consistency strategy must also be designed: explicit `Remove` over Redis, pub/sub invalidation messages, or tag-based expiry with `HybridCache` (.NET 9+). Changing the interface alone does not resolve all consistency issues in a multi-replica deployment.

---

## 6. Architecture Decisions

### 6.1 Controller / Service / Domain separation

**Decision: Controllers contain only HTTP binding and result mapping; all business logic lives in Services.**

The `RequestsController` receives a DTO, passes a `CancellationToken`, calls the appropriate service, and maps the discriminated-union result to an HTTP status code. It contains no `if` statements over domain conditions.

Services return a result type (`StatusUpdateResult.Success`, `.NotFound`, `.InvalidTransition`, `.Conflict`) rather than throwing exceptions for expected outcomes. This avoids using exceptions for flow control and makes the controller mapping mechanical.

**Alternative considered:** MediatR / CQRS — adds dispatch infrastructure and separate Request/Handler types. For a single controller with five endpoints, the overhead is not justified. The direct service call is simpler to follow and easier to test.

### 6.2 No Repository Pattern over EF Core

**Decision: `DbContext` is used directly in Services; no generic or specific repository layer.**

`DbContext` already implements the Unit of Work and Repository patterns. Adding a `IRequestRepository` above it would be a thin wrapper with no added value at this scale.

**Alternative considered:** A repository interface for testability. The integration tests use `WebApplicationFactory` against a real SQL Server database, which removes the need for a faked repository. Unit tests cover only pure domain logic (`StatusTransitionPolicy`) which has no data access dependency.

### 6.3 CancellationToken propagation

Every `async` method in both services accepts a `CancellationToken` passed from the HTTP request's `RequestAborted` token. If the client disconnects, in-flight EF queries are cancelled. `OperationCanceledException` is handled in the global exception handler at a low log level — it is not an application error.

---

## 7. Known Limitations and Potential Improvements

| Limitation | Improvement |
|---|---|
| `LIKE '%term%'` free-text search causes a full clustered index scan | Add a Full-Text Index and replace `Contains` with `EF.Functions.FreeText` or `EF.Functions.Contains` |
| `OFFSET/FETCH` deep pagination degrades linearly with page depth | Switch to Keyset (cursor) Pagination with a composite cursor matching the existing sort: `WHERE (CreatedAt < @lastCreatedAt) OR (CreatedAt = @lastCreatedAt AND Id < @lastId) ORDER BY CreatedAt DESC, Id DESC` |
| Bulk update: up to 100 `SaveChanges` round-trips | Single-transaction batch with `ex.Entries` triage on `DbUpdateConcurrencyException` |
| `IMemoryCache` per-process: summary stale up to 60 s per instance in multi-replica deployment | Replace with `IDistributedCache` (Redis) |
| No authentication: `changedBy` is an unchecked plain string | Add bearer token authentication; derive `changedBy` from the token claims |
| Integration tests require a SQL Server instance | Add Testcontainers support for Docker-based CI (requires Docker on test agents) |

---

## 8. AI-Assisted Engineering Workflow

This project was built using **Claude Code (Claude Sonnet 4.6)** as an engineering assistant throughout. The developer directed the work; Claude Code assisted with implementation, testing infrastructure, performance measurement, and documentation.

### Developer role

- **Requirements and planning:** The developer read the assignment specification, decomposed it into tasks (`TASKS.md`), set priorities, and defined acceptance criteria. All task sequencing and architectural constraints were developer decisions.


- **Review and verification:** The developer reviewed and understood most generated code and verified application behavior before each commit. Application-level correctness (API responses, filter behavior, Concurrency conflicts, Cache invalidation) was confirmed through manual testing in Swagger and browser DevTools.
- **Evaluation and correction:** The developer evaluated results, raised technical questions, and requested changes when generated output was incorrect or inconsistent. Several documentation corrections were applied across multiple sessions.

### AI-assisted implementation

Claude Code generated or substantially drafted the following:

- **Backend:** Entity models, EF Core Fluent configurations, migrations, `RequestQueryService`, `RequestStatusService`, DTOs, `RequestsController`, global exception handler, `DataSeeder`.
- **Tests:** xUnit unit test files, integration test infrastructure (`ApiFactory`, `DatabaseCollection`), integration test classes (`ConcurrencyTests`, `GetRequestsFilteringTests`).
- **Frontend:** Angular project setup, `RequestsApiService`, `RequestsPageComponent` (RxJS pipeline with debounce and switchMap), `SummaryPanelComponent`, status update flow with 409 handling, history panel.
- **Documentation:** `TASKS.md`, `README.md`, `docs/performance.md`, this document.

### Concrete example: T-801 performance measurement

T-801 required measuring two API operations against 100,000 records and documenting execution plans, logical reads, and index trade-offs.

**What Claude Code did:** executed `SET STATISTICS IO, TIME ON` and `SET STATISTICS PROFILE ON` measurement scripts on the developer's local SQL Server instance and reported the raw output. Ran a covering-index experiment — creating the experimental index, measuring, and dropping it — and documented the before/after results.

**What the developer directed and reviewed:**
- Specified which queries to measure and which filter parameters to use.
- Directed the covering-index experiment as a hypothesis to test.
- Reviewed the results: baseline of **1,510 logical reads** for page 1 under a clustered scan; experimental covering index reducing reads to **5** (−99.7%) for page 1, **133** (−91.2%) for page 500.
- Evaluated the trade-offs (estimated ~46 MB index size, higher write overhead) against the measured gains.
- Confirmed that the temporary experimental index was dropped after measurement and that the database was returned to its original state.
- Reviewed the accuracy of performance documentation corrections (ratio errors, estimate labeling, optimizer-estimate phrasing) applied in a subsequent session.

The measured values in `docs/performance.md` reflect actual SQL Server output captured on the developer's machine; none are estimated or fabricated.

### Defects encountered during development

Three defects were identified and resolved during development. Their root causes are documented in `TASKS.md §6.1`. The `TASKS.md` work log records that BF-1 (`distinctUntilChanged` suppression) was "identified via manual test (search worked once then stopped)" and that BF-2 was traced via DevTools. The precise division of discovery effort between developer observation and AI analysis is not separately recorded.

| ID | Defect | Resolution |
|---|---|---|
| BF-1 | Angular search stopped emitting after the first input | Removed `distinctUntilChanged` from the `Subject<void>` pipeline |
| BF-2 | Two HTTP calls per filter change due to duplicate `async` pipe subscriptions | Wrapped template in a single outer `@if` |
| BF-3 | `createdTo` filter excluded records created after midnight on the boundary date | Changed to exclusive upper bound `createdTo + 1 day` with `<` (sargable) |

### Transparency statement

This workflow represents human-directed, AI-assisted engineering: the developer set the requirements, made the technical decisions, understood the implementation, and verified behavior; Claude Code handled the execution-heavy parts — code generation, test scaffolding, SQL script execution, and documentation drafting. The distinction is maintained here to satisfy R-11.9 honestly rather than either overstating manual authorship or understating developer responsibility.
