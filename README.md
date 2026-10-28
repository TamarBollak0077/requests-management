# Requests Management

## תקציר הפרויקט

מערכת Full Stack לניהול פניות ובקשות, שפותחה במסגרת מבדק מקצועי למשרת מפתח/ת תוכנה רמה ב׳.

המערכת מבוססת על Angular, ASP.NET Core Web API ו-SQL Server, וכוללת 100,000 רשומות בדיקה לצורך בחינת ביצועים ועבודה עם נתונים בהיקף גדול.

הפתרון כולל סינון, מיון ודפדוף בצד השרת, מנגנון Optimistic Concurrency, היסטוריית שינויים, עדכון סטטוס למספר פניות, Cache, בדיקות אוטומטיות ומדידת ביצועים.

תהליך הפיתוח התבסס על חלוקה למשימות, בחינת החלטות טכנולוגיות ושימוש ב-Claude Code ככלי עזר לפיתוח, לצד סקירת קוד ובדיקת התנהגות המערכת.

הוראות ההתקנה, ההרצה והתיעוד הטכני המלא מופיעים בהמשך באנגלית.

---

A Full Stack application for managing requests at scale. Built as a home exam for a Full Stack Developer (Level B) role.

## Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Backend | ASP.NET Core Web API | .NET 8 |
| ORM | Entity Framework Core | 8.x |
| Database | SQL Server | 2019 / 2022 |
| Frontend | Angular (Standalone Components) | 20.x |
| Tests | xUnit | 2.5.3 |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server 2019 or later (Express edition is fine; LocalDB was not tested and is not recommended — the project was developed and validated against a full SQL Server instance)
- [Node.js](https://nodejs.org/) 20.19 or later (required by Angular 20)
- [Angular CLI](https://angular.dev/tools/cli): `npm install -g @angular/cli`
- EF Core CLI tools: `dotnet tool install --global dotnet-ef`

Verify your versions:

```bash
dotnet --version      # must be 8.x
node --version        # must be >= 20.19
npm --version
ng version
dotnet ef --version
```

---

## Clone and Install

```bash
git clone https://github.com/TamarBollak0077/requests-management.git
cd requests-management
```

Install frontend dependencies:

```bash
cd frontend/requests-app
npm install
cd ../..
```

> **Working directory convention:** All commands in the sections below assume you start from the repository root (`requests-management/`). Each `cd backend` or `cd frontend/requests-app` navigates from there. Open a fresh terminal at the repo root for each section if commands are run out of order.

---

## SQL Server Connection

The default connection string targets a local SQL Server instance using Windows Authentication:

```
Server=(local);Database=RequestsManagement;Integrated Security=True;TrustServerCertificate=True;
```

This is defined in `backend/src/RequestsManagement.Api/appsettings.json`.

To use a different server or SQL authentication, use the .NET user-secrets store so credentials are never written to a tracked file (`appsettings.Development.json` is **not** in `.gitignore` for this project):

```bash
# Bash / zsh
cd backend/src/RequestsManagement.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=YOUR_SERVER;Database=RequestsManagement;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
```

```powershell
# PowerShell
cd backend/src/RequestsManagement.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=RequestsManagement;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
```

Alternatively, set an environment variable before running the API:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=YOUR_SERVER;Database=RequestsManagement;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
```

---

## EF Core Migrations

Create the database and apply all migrations from the `backend/` directory:

```bash
cd backend
dotnet ef database update --project src/RequestsManagement.Api
```

This creates the `RequestsManagement` database with three migrations:

- `InitialCreate` — `Requests` table and `RequestStatusHistory` table
- `AddIndexes` — all performance indexes (see [docs/performance.md](docs/performance.md))
- `AddCoveringIndex` — covering index `IX_Requests_Status_CreatedAt_Covering` with INCLUDE columns for the full list-item projection (see [docs/performance.md](docs/performance.md) §13)

---

## Database Seeding (100,000 Records)

The seeder is invoked as a CLI argument. It **deletes existing rows** and reinserts 100,000 deterministic records (fixed random seed 42).

```bash
cd backend
dotnet run --project src/RequestsManagement.Api -- --seed
```

To seed a different count:

```bash
dotnet run --project src/RequestsManagement.Api -- --seed 50000
```

The process exits after seeding; the web server does not start. Rerunning produces exactly the same 100,000 rows (reproducible). Elapsed time is printed to stdout.

---

## Running the Backend

```bash
cd backend
dotnet run --project src/RequestsManagement.Api
```

The API starts at:

| URL | Description |
|---|---|
| `http://localhost:5214` | HTTP endpoint |
| `https://localhost:7100` | HTTPS endpoint |
| `http://localhost:5214/swagger` | Swagger UI |

---

## API Reference

| Method | Route | Description | Response codes |
|---|---|---|---|
| GET | `/api/requests` | Filtered, sorted, paginated list | 200, 400 |
| GET | `/api/requests/{id}` | Single request detail | 200, 404 |
| GET | `/api/requests/summary` | Aggregations by Status and Priority (cached) | 200 |
| PATCH | `/api/requests/{id}/status` | Update status with optimistic concurrency | 200, 400, 404, 409 |
| GET | `/api/requests/{id}/history` | Status change history, newest first | 200, 404 |
| POST | `/api/requests/status/bulk` | Update up to 100 requests in one call (partial success) | 200, 400 |

Query parameters for `GET /api/requests`:

| Parameter | Type | Notes |
|---|---|---|
| `page` | int | ≥ 1, default 1 |
| `pageSize` | int | 1–100, default 20 |
| `status` | string[] | `New`, `InProgress`, `Waiting`, `Completed` |
| `priority` | string[] | `Low`, `Medium`, `High` |
| `organizationName` | string | StartsWith filter (index-backed) |
| `assignedTo` | string | Exact match |
| `createdFrom` | date | Inclusive lower bound (YYYY-MM-DD) |
| `createdTo` | date | Inclusive upper bound (YYYY-MM-DD) |
| `search` | string | Contains search on Title and OrganizationName |
| `sortBy` | string | `createdAt` (default), `priority`, `title` |
| `sortDirection` | string | `desc` (default) or `asc` |

---

## Running the Frontend

With the backend running, start Angular's dev server in a second terminal:

```bash
cd frontend/requests-app
npm start
```

The UI is available at `http://localhost:4200`.

All `/api/*` requests are proxied to `http://localhost:5214` via `proxy.conf.json`. No CORS configuration is needed for local development.

---

## Running the Tests

Integration tests require a separate SQL Server database named `RequestsManagement_Tests`. The test infra runs migrations automatically on first run.

```bash
cd backend
dotnet test
```

To override the test connection string (e.g. on a CI server or a different SQL instance):

```powershell
$env:TEST_CONNECTION_STRING = "Server=YOUR_SERVER;Database=RequestsManagement_Tests;Integrated Security=True;TrustServerCertificate=True;"
dotnet test
```

> The database name **must** be `RequestsManagement_Tests`. The factory validates this and refuses to run against any other database to prevent accidental data loss.

Test breakdown:

| Project | Kind | File |
|---|---|---|
| `RequestQueryParametersTests` | Unit | Query parameter validation (pageSize, sortBy, date range) |
| `StatusTransitionPolicyTests` (in UnitTest1.cs) | Unit | Allowed and forbidden status transitions |
| `GetRequestsFilteringTests` | Integration | Filtering, sorting, pagination against a real DB |
| `ConcurrencyTests` | Integration | Sequential 409 conflict and parallel `Task.WhenAll` conflict; exactly one audit row created |
| `StatusUpdateEdgeCaseTests` | Integration | 404 (not found), 400 (invalid transition), and bulk partial-success outcomes |
| `CacheInvalidationTests` | Integration | Summary cache invalidated on status update; stale value not returned after change |
| `SmokeTests` | Integration | Basic API health |

---

## Project Structure

```
RequestsManagement/
├── README.md                          This file
├── TASKS.md                           Work plan, task breakdown, traceability
├── docs/
│   ├── performance.md                 Query plans, index benchmarks, bottlenecks
│   └── decisions.md                   DB choice, Concurrency, Bulk, Cache, AI usage
├── backend/
│   ├── RequestsManagement.sln
│   ├── src/RequestsManagement.Api/
│   │   ├── Controllers/               RequestsController — HTTP binding only, no business logic
│   │   ├── Services/                  RequestQueryService, RequestStatusService
│   │   ├── Domain/                    Request, RequestStatusHistory, enums, StatusTransitionPolicy
│   │   ├── Data/                      AppDbContext, Fluent Configurations, Migrations, DataSeeder
│   │   ├── Dtos/                      Request/Response DTOs (no AutoMapper — LINQ Projection)
│   │   ├── Cache/                     SummaryCacheOptions, CacheKeys
│   │   ├── Infrastructure/            GlobalExceptionHandler (ProblemDetails, no stack traces in prod)
│   │   └── Program.cs                 DI registration, middleware, seed mode
│   └── tests/RequestsManagement.Tests/
│       ├── Infrastructure/            ApiFactory (WebApplicationFactory), DatabaseCollection
│       ├── Integration/               ConcurrencyTests, GetRequestsFilteringTests, SmokeTests
│       └── Unit (inline)/             StatusTransitionPolicy, RequestQueryParameters validation
└── frontend/requests-app/             Angular 20, Standalone Components
    └── src/app/
        ├── core/
        │   ├── models/                DTO interfaces
        │   └── services/              RequestsApiService (HttpClient only)
        └── features/requests/
            ├── requests-page.component  Container: RxJS state, debounce(300ms), switchMap
            ├── summary-panel/           Aggregations display
            └── (filters, table, history inline in requests-page.component)
```

---

## Assumptions and Known Limitations

### Assumptions (gaps not defined in the exam PDF)

| Topic | Assumption |
|---|---|
| Status transitions | `New→InProgress/Waiting`, `InProgress→Waiting/Completed`, `Waiting→InProgress/Completed`. `Completed` is terminal. Transitioning to the same status is rejected. |
| `changedBy` field | No authentication is required. The caller must supply a `changedBy` string in the request body (validated, non-empty). The UI has a "current user" text field. |
| Date range filter | Applied to `CreatedAt`. `createdTo` is inclusive (implemented as exclusive upper bound `createdTo + 1 day` to include records created at any time on that date). |
| `organizationName` filter vs. search | Filter = `StartsWith` (index-backed). Free-text `search` = `Contains` on Title + OrganizationName (non-sargable, see bottlenecks). |
| Aggregations | Count by Status and count by Priority, both globally. Cached for 60 seconds. |
| Page size limit | Default 20, maximum 100. |

### Known Limitations

| Area | Limitation |
|---|---|
| Authentication | No authentication or authorization. `changedBy` is a plain string. |
| `search` (Contains) | Translates to `LIKE '%x%'` — non-sargable, causes an index scan on large datasets. Improvement: Full-Text Index. |
| Deep pagination | `COUNT(*)` + `OFFSET` degrades on page > ~500. Improvement: Keyset Pagination. |
| Bulk Update round-trips | Each item in a bulk request triggers a separate `SaveChanges`. Up to 100 round-trips per call. Improvement: batched save with `ex.Entries` handling. |
| Cache scale-out | `IMemoryCache` is per-process. In a multi-instance deployment, invalidation in one instance does not reach others. Improvement: `IDistributedCache` (Redis). |
| Integration tests require SQL Server | No Docker / Testcontainers. The reviewer must have SQL Server accessible. The connection string is configurable via the `TEST_CONNECTION_STRING` environment variable. |
| Angular tests | No automated Angular tests (not required by the exam). |

---

## Project Planning and Tracking

- **[GitHub Issues](https://github.com/TamarBollak0077/requests-management/issues)** — one issue per task; [closed issues](https://github.com/TamarBollak0077/requests-management/issues?q=is%3Aissue+is%3Aclosed) show completed work. Issue #27 ([T-706] Bulk UI) is intentionally left open as a deferred P3 enhancement.
- **[GitHub Project #1](https://github.com/users/TamarBollak0077/projects/1)** (`Requests Management`) — Kanban board tracking issue status.
- **[TASKS.md](TASKS.md)** — Requirement traceability (R-x.y IDs mapped to task IDs), dependency ordering, effort estimates, and a work-log section. This is the primary planning document.

---

## Related Documentation

- [docs/performance.md](docs/performance.md) — Query plans, logical reads, index benchmarks, bottlenecks
- [TASKS.md](TASKS.md) — Full work plan with task breakdown, status, and traceability to exam requirements
- [docs/decisions.md](docs/decisions.md) — DB choice, Concurrency design, Bulk strategy, Cache design, AI usage log
