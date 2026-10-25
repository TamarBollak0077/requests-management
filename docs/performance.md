# Performance Measurement — docs/performance.md

## 1. Test environment

| Property | Value |
|---|---|
| Server | DESKTOP-1VUANBN (local SQL Server) |
| Database | RequestsManagement |
| Row count | 100,000 requests |
| Status distribution | New: 25,010 · InProgress: 25,154 · Waiting: 24,886 · Completed: 24,950 |
| CreatedAt range | 2024-01-01 → 2025-12-30 |
| EF Core version | 8.x |
| Measurement tool | sqlcmd, `SET STATISTICS IO ON`, `SET STATISTICS TIME ON`, `SET STATISTICS PROFILE ON` |
| Methodology | 3 warm-cache runs per query; median reported; no estimated values |

---

## 2. Operations measured

Two production API operations:

| Label | API endpoint | Description |
|---|---|---|
| **GET /api/requests** | `GET /api/requests?status=InProgress&createdFrom=2024-06-01&createdTo=2025-06-30&sortBy=createdAt&sortDirection=desc` | Filtered, sorted, paginated list |
| **GET /api/requests/summary** | `GET /api/requests/summary` | Aggregated counts by Status and Priority |

Filter parameters used in all measurements:

```sql
@status      tinyint   = CAST(1 AS tinyint)   -- InProgress
@createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z'
@upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z'  -- exclusive (createdTo 2025-06-30 + 1 day)
```

Filtered result set: **13,590 rows** — 680 pages at pageSize=20.

---

## 3. Exact SQL (verified from EF Core log)

### A-COUNT — runs first inside every GET /api/requests call

```sql
SELECT COUNT(*)
FROM [Requests] AS [r]
WHERE [r].[Status]    = @status
  AND [r].[CreatedAt] >= @createdFrom
  AND [r].[CreatedAt] <  @upperBound
```

### Query A — page 1 (OFFSET 0)

```sql
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM [Requests] AS [r]
WHERE [r].[Status]    = @status
  AND [r].[CreatedAt] >= @createdFrom
  AND [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY
```

### Query B — page 500 (deep page, OFFSET 9980)

```sql
-- same as Query A, OFFSET 9980 ROWS FETCH NEXT 20 ROWS ONLY
```

### Summary — two GROUP BY queries

```sql
-- C1
SELECT [r].[Status], COUNT(*) AS [Count]
FROM [Requests] AS [r] GROUP BY [r].[Status]

-- C2
SELECT [r].[Priority], COUNT(*) AS [Count]
FROM [Requests] AS [r] GROUP BY [r].[Priority]
```

> **Note on `@upperBound`:** The production code translates `createdTo=2025-06-30` to `CreatedAt < '2025-07-01'` (exclusive upper bound) so that datetime values after midnight on June 30 are included. The constant is computed in C#, keeping the column free of functions and the filter sargable.

---

## 4. Index inventory

All indexes were enabled throughout. The migration `AddIndexes` created these nonclustered indexes:

| Index | Key columns | Purpose |
|---|---|---|
| `IX_Requests_Status_CreatedAt` | Status ASC, CreatedAt DESC | Status filter + default sort; COUNT query |
| `IX_Requests_Priority_CreatedAt` | Priority ASC, CreatedAt DESC | Priority filter; GROUP BY Priority |
| `IX_Requests_AssignedTo_CreatedAt` | AssignedTo ASC, CreatedAt DESC | AssignedTo filter |
| `IX_Requests_CreatedAt` | CreatedAt DESC | Date-range-only queries |
| `IX_Requests_OrganizationName` | OrganizationName ASC | OrganizationName prefix filter |

All key columns include `CreatedAt DESC` as the trailing key so that `ORDER BY CreatedAt DESC` queries avoid a sort operator when the leading filter is an equality predicate.

---

## 5. Baseline measurements — existing indexes (Step 2)

*(Median of 3 warm-cache runs)*

| Query | Logical reads | CPU (ms) | Elapsed (ms) | Index used |
|---|---|---|---|---|
| A-COUNT | 79 | 16 | 7 | `IX_Requests_Status_CreatedAt` |
| **Query A page 1** | **1,510** | **78** | **83** | `PK_Requests` (clustered scan) |
| **Query B page 500** | **1,510** | **78** | **83** | `PK_Requests` (clustered scan) |
| C1 GROUP BY Status | 558 | 78 | 72 | `IX_Requests_Status_CreatedAt` |
| C2 GROUP BY Priority | 547 | 62 | 62 | `IX_Requests_Priority_CreatedAt` |

---

## 6. Actual execution plans — baseline

### A-COUNT
```
Index Seek(IX_Requests_Status_CreatedAt)
  SEEK: Status=1 AND CreatedAt >= @from AND CreatedAt < @upper
  → Stream Aggregate(COUNT(*)) → Compute Scalar
```
Efficient: seek navigates directly to the 13,590 matching entries in the narrow index (79 pages).

### Query A and B — bottleneck
```
Clustered Index Scan(PK_Requests)  [13,590 rows, WHERE applied as residual]
  → Sort(CreatedAt DESC, Id DESC)   [sort all 13,590 rows in memory]
  → Top(OFFSET n, FETCH 20)
```

**Root cause:** `IX_Requests_Status_CreatedAt` is not a covering index. The SELECT projects 9 columns (`Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion`). The nonclustered index stores only `(Status, CreatedAt, Id)`. The optimizer estimated that retrieving the remaining 6 columns via the nonclustered index would require up to one Key Lookup per matched row (~13,590 lookups for this filter), and determined that a single clustered scan + sort is cheaper. The plan that actually executed is the clustered scan; the Key Lookup plan was costed and rejected.

Consequence: **both page 1 and page 500 produce 1,510 logical reads**, because the clustered scan always processes all 13,590 matching rows before Sort+Top apply.

### C1 and C2 — efficient
```
Index Scan(IX_Requests_Status_CreatedAt, ORDERED FORWARD)   -- C1
  → Stream Aggregate(GROUP BY Status)

Index Scan(IX_Requests_Priority_CreatedAt, ORDERED FORWARD) -- C2
  → Stream Aggregate(GROUP BY Priority)
```
Both indexes cover their respective `SELECT key, COUNT(*)` projections; no clustered access needed.

---

## 7. ALTER INDEX DISABLE / REBUILD comparison

**Index tested:** `IX_Requests_Status_CreatedAt`  
**Query:** A-COUNT (`SELECT COUNT(*) WHERE Status=1 AND CreatedAt range`)

The index was disabled, measured, then immediately rebuilt (finally-style — rebuild runs regardless of measurement outcome).

| State | Logical reads | CPU (ms) | Elapsed (ms) | Plan |
|---|---|---|---|---|
| **Index enabled** (Step 2 baseline) | **79** | 16 | 7 | Index Seek(IX_Requests_Status_CreatedAt) |
| **Index disabled** (this step) | **1,510** | 47 | 54 | Clustered Index Scan(PK_Requests) |

Without `IX_Requests_Status_CreatedAt`, the optimizer falls back to scanning the entire 100,000-row clustered index with a residual WHERE filter — **19× more logical reads** for the COUNT query.

**REBUILD result:** All 8 original indexes confirmed enabled after rebuild (is_disabled = 0 on all).

---

## 8. Covering index experiment (Step 3)

A temporary experimental index was created, measured, then **dropped**:

```sql
CREATE NONCLUSTERED INDEX IX_Requests_Status_CreatedAt_Covering_Exp
ON dbo.Requests (Status ASC, CreatedAt DESC, Id DESC)
INCLUDE (Title, OrganizationName, Priority, AssignedTo, UpdatedAt, RowVersion);

-- ... measurements ...

DROP INDEX IF EXISTS IX_Requests_Status_CreatedAt_Covering_Exp ON dbo.Requests;
```

### Before / after (medians)

| Query | Baseline | With covering index | Logical reads Δ |
|---|---|---|---|
| A-COUNT | 79 reads · 7 ms | 79 reads · 7 ms | **0** |
| **Query A page 1** | **1,510 · 83 ms** | **5 · < 1 ms** | **−1,505 (−99.7%)** |
| **Query B page 500** | **1,510 · 83 ms** | **133 · 7 ms** | **−1,377 (−91.2%)** |
| C1 Summary/Status | 558 · 72 ms | 558 · 62 ms | 0 |
| C2 Summary/Priority | 547 · 62 ms | 547 · 64 ms | 0 |

### Execution plan with covering index

```
Index Seek(IX_Requests_Status_CreatedAt_Covering_Exp)
  SEEK: Status=1 AND CreatedAt range   [all 9 columns available at leaf]
  → Top(OFFSET n, FETCH 20)
```

- **Sort operator: absent** — index key `(Status, CreatedAt DESC, Id DESC)` matches `ORDER BY CreatedAt DESC, Id DESC` exactly; output is pre-ordered.
- **Key Lookups: none** — INCLUDE columns cover the full SELECT projection.
- **A-COUNT unchanged** — the optimizer prefers the narrower `IX_Requests_Status_CreatedAt` for the COUNT (no projected columns needed, smaller leaf rows, same seek).

### Why page 500 costs more than page 1

OFFSET-based pagination must traverse the index from position 0 to position OFFSET before returning rows. Page 500 (OFFSET 9,980) reads 10,000 index leaf entries before emitting 20, vs. 20 for page 1. This is an inherent limitation of `OFFSET/FETCH` — logical reads scale with depth (133 vs. 5). The covering index does not eliminate this, but reduces per-read cost dramatically: page 1 drops from 1,510 to 5 reads (~302× improvement), and page 500 drops from 1,510 to 133 reads (~11× improvement), so even deep pages are far faster than the baseline clustered scan.

---

## 9. Index trade-offs and recommendation

| Aspect | Key-only `IX_Requests_Status_CreatedAt` | Covering variant (+INCLUDE) |
|---|---|---|
| Leaf row size (est.) | ~13 bytes (Status 1 + CreatedAt 8 + Id 4) | ~484 bytes (+Title 200 + OrgName 150 + Priority 1 + AssignedTo 100 + UpdatedAt 8 + RowVersion 8) |
| Index size (est.) | ~1.2 MB | ~46 MB |
| Page 1 reads | 1,510 (clustered scan) | 5 (seek, 20 rows) |
| Page 500 reads | 1,510 (clustered scan) | 133 (seek, 10,000 rows) |
| COUNT reads | 79 | 79 (optimizer still uses key-only for COUNT) |
| Write overhead | Low | Higher — every INSERT/UPDATE touching INCLUDE columns must update both index structures |
| Sort on every query | Yes (in-memory, 13,590 rows) | No (eliminated by key order) |

**Recommendation:** Make the existing index covering by adding `Id DESC` to the key and the 6 projected columns to INCLUDE, replacing the key-only index via a single `DROP_EXISTING = ON` migration. The estimated ~45 MB storage cost and marginally higher write overhead are acceptable for a read-dominant use case at 100,000 rows. This is a straightforward EF Core migration change.

**Important caveat:** In the covering-index experiment the optimizer chose the narrower key-only index for the COUNT query (§8 table: A-COUNT unchanged at 79 reads). If the key-only index is replaced rather than supplemented, the optimizer will have only the larger covering index available for that query. COUNT performance should be re-measured after the migration to confirm the 79-read baseline is maintained; the covering index's larger leaf rows (~484 bytes vs. ~13 bytes) could increase logical reads for that path.

---

## 10. Known bottleneck: free-text search

`GET /api/requests?search=term` translates to:

```sql
WHERE Title LIKE '%term%' OR OrganizationName LIKE '%term%'
```

A leading wildcard prevents a standard B-tree index seek on either column — the optimizer has no sargable predicate and will typically scan the entire clustered index (all 100,000 rows in the current dataset). This is documented as a known limitation. Full-text indexes (`CREATE FULLTEXT INDEX`) would allow efficient keyword lookup and are outside the current scope.

---

## 11. Database restoration verification

All experimental changes were fully reverted after measurement:

| Change | Reverted? | Verification |
|---|---|---|
| Covering index created (Step 3) | Yes — `DROP INDEX IF EXISTS` | Index absent from `sys.indexes` |
| `IX_Requests_Status_CreatedAt` disabled (DISABLE/REBUILD step) | Yes — `ALTER INDEX ... REBUILD` | `is_disabled = 0` confirmed |

Final index count: **8** (same as before any experiment). All `is_disabled = 0`.

---

## 12. Source files

| File | Description |
|---|---|
| `docs/t801_step2_measure.sql` | Baseline measurement script |
| `docs/t801_step2_raw.txt` | Baseline raw output |
| `docs/t801_step2_results.md` | Baseline summary |
| `docs/t801_step3_measure.sql` | Covering-index measurement script |
| `docs/t801_step3_raw.txt` | Covering-index raw output |
| `docs/t801_step3_plans.txt` | Actual execution plans (Step 3) |
| `docs/t801_step3_results.md` | Covering-index summary |
| `docs/t801_disable_rebuild.sql` | DISABLE/REBUILD script |
| `docs/t801_disable_rebuild_raw.txt` | DISABLE/REBUILD raw output |
