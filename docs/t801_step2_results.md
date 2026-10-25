# T-801 Step 2 — Baseline Performance Measurements (Existing Indexes)

**Date:** 2026-10-25  
**Server:** (local) SQL Server  
**Database:** RequestsManagement  
**Rows:** 100,000 in dbo.Requests  
**Filter:** Status = 1 (InProgress), CreatedAt 2024-06-01 → 2025-06-30 (exclusive upper bound 2025-07-01)  
**Filtered rows:** 13,590 · 680 pages at pageSize=20  
**Deep page:** 500 (OFFSET 9980)

---

## 1. Index inventory (all enabled)

| Index | Type | Columns | is_disabled |
|---|---|---|---|
| PK_Requests | Clustered | Id | 0 |
| IX_Requests_Status_CreatedAt | Nonclustered | Status ASC, CreatedAt DESC | 0 |
| IX_Requests_Priority_CreatedAt | Nonclustered | Priority ASC, CreatedAt DESC | 0 |
| IX_Requests_AssignedTo_CreatedAt | Nonclustered | AssignedTo ASC, CreatedAt DESC | 0 |
| IX_Requests_CreatedAt | Nonclustered | CreatedAt DESC | 0 |
| IX_Requests_OrganizationName | Nonclustered | OrganizationName ASC | 0 |

---

## 2. Raw measurements (3 runs each)

### A-COUNT — `SELECT COUNT(*) WHERE Status=1 AND CreatedAt …`

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 79 | 16 | 23 |
| 2 | 79 | 0 | 6 |
| 3 | 79 | 16 | 7 |
| **Median** | **79** | **16** | **7** |

### Query A — page 1 (OFFSET 0, FETCH 20)

| Run | Logical reads (Requests) | Worktable reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|---|
| 1 | 1510 | 0 | 78 | 175 *(cold — physical reads)* |
| 2 | 1510 | 0 | 62 | 86 |
| 3 | 1510 | 0 | 78 | 83 |
| **Median** | **1510** | **0** | **78** | **83** |

### Query B — page 500 (OFFSET 9980, FETCH 20)

| Run | Logical reads (Requests) | Worktable reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|---|
| 1 | 1510 | 0 | 63 | 82 |
| 2 | 1510 | 0 | 94 | 85 |
| 3 | 1510 | 0 | 78 | 83 |
| **Median** | **1510** | **0** | **78** | **83** |

### Query C1 — `GROUP BY Status`

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 558 | 78 | 72 |
| 2 | 558 | 62 | 65 |
| 3 | 558 | 79 | 78 |
| **Median** | **558** | **78** | **72** |

### Query C2 — `GROUP BY Priority`

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 547 | 62 | 96 *(cold — physical reads)* |
| 2 | 547 | 63 | 62 |
| 3 | 547 | 62 | 59 |
| **Median** | **547** | **62** | **62** |

---

## 3. Median summary table

| Query | API endpoint | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|---|
| A-COUNT | GET /api/requests (count sub-query) | 79 | 16 | 7 |
| A Page 1 | GET /api/requests page=1 | 1510 | 78 | 83 |
| B Page 500 | GET /api/requests page=500 | 1510 | 78 | 83 |
| C1 Status | GET /api/requests/summary | 558 | 78 | 72 |
| C2 Priority | GET /api/requests/summary | 547 | 62 | 62 |

---

## 4. Actual execution plans

### A-COUNT
```
Index Seek(IX_Requests_Status_CreatedAt)
  → Stream Aggregate(Count(*))
  → Compute Scalar
```
- **IX_Requests_Status_CreatedAt** used as an **Index Seek** on `Status = @status AND CreatedAt BETWEEN @createdFrom AND @upperBound`.
- Efficient: only 79 logical reads for 13,590 matching rows.

### Query A (page 1) and Query B (page 500)
```
Clustered Index Scan(PK_Requests) [WHERE Status=1, CreatedAt range]
  → Sort(CreatedAt DESC, Id DESC)
  → Top(OFFSET n, FETCH 20)
```
- **PK_Requests (clustered)** is scanned — the nonclustered index `IX_Requests_Status_CreatedAt` is **not used**.
- **Root cause:** the SELECT projects 9 columns (Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion). The nonclustered index only stores (Status, CreatedAt, Id); every row would require a Key Lookup to fetch the remaining columns. The optimizer determined that 13,590 Key Lookups cost more than a single clustered scan + sort.
- The Sort node processes the same 13,590 rows for both page 1 and page 500 (the Clustered Index Scan always reads the whole matching set). Page 500 passes 10,000 rows through the Sort before emitting 20 (OFFSET 9980 + FETCH 20), while page 1 passes 20.
- 1510 logical reads = all clustered index pages covering the 13,590 matching rows.

### Query C1 (GROUP BY Status)
```
Index Scan(IX_Requests_Status_CreatedAt, ORDERED FORWARD)
  → Stream Aggregate(GROUP BY Status)
  → Compute Scalar
```
- Narrow index (Status, CreatedAt) covers the `SELECT Status, COUNT(*)` projection entirely — no clustered access.
- Scans all 100,000 index entries; 558 logical reads.

### Query C2 (GROUP BY Priority)
```
Index Scan(IX_Requests_Priority_CreatedAt, ORDERED FORWARD)
  → Stream Aggregate(GROUP BY Priority)
  → Compute Scalar
```
- Covered by `IX_Requests_Priority_CreatedAt`; 547 logical reads.

---

## 5. Key finding

**Queries A and B do not use `IX_Requests_Status_CreatedAt` for the paged SELECT.**  
The optimizer chooses `PK_Requests` (Clustered Index Scan) + Sort because the nonclustered index is not covering — it would require 13,590 Key Lookups to fetch the projected columns. The fix is to add the projected columns as INCLUDE columns on `IX_Requests_Status_CreatedAt`, making it a covering index for this query shape.

---

## 6. Source files

| File | Description |
|---|---|
| `docs/t801_step2_measure.sql` | Reproducible measurement script (3 runs × 5 queries) |
| `docs/t801_step2_raw.txt` | Raw STATISTICS IO/TIME output from the measurement run |
| `docs/t801_plan_acount.txt` | Actual execution profile for A-COUNT |
| `docs/t801_plan_qa.txt` | Actual execution profile for Query A (page 1) |
| `docs/t801_plan_qb_qc.txt` | Actual execution profiles for Query B (page 500) and C1/C2 |
| `docs/t801_step2_plans.sql` | Script to re-run with STATISTICS XML ON (for SSMS graphical plans) |
