# T-801 Step 3 — Covering Index Experiment

**Date:** 2026-10-25  
**Server:** (local) SQL Server  
**Database:** RequestsManagement  
**Rows:** 100,000 in dbo.Requests  
**Experimental index:** `IX_Requests_Status_CreatedAt_Covering_Exp` (created, measured, dropped)

---

## Experimental index definition

```sql
CREATE NONCLUSTERED INDEX IX_Requests_Status_CreatedAt_Covering_Exp
ON dbo.Requests
    (Status ASC, CreatedAt DESC, Id DESC)
INCLUDE (Title, OrganizationName, Priority, AssignedTo, UpdatedAt, RowVersion);
```

---

## Raw measurements — 3 runs each (Step 3, with covering index)

### A-COUNT

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 79 | 16 | 18 |
| 2 | 79 | 0 | 7 |
| 3 | 79 | 15 | 6 |
| **Median** | **79** | **15** | **7** |

### Query A — page 1 (OFFSET 0)

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 5 | 0 | 0 |
| 2 | 5 | 0 | 0 |
| 3 | 5 | 0 | 0 |
| **Median** | **5** | **0** | **0** |

### Query B — page 500 (OFFSET 9980)

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 133 | 0 | 7 |
| 2 | 133 | 0 | 7 |
| 3 | 133 | 0 | 6 |
| **Median** | **133** | **0** | **7** |

### Query C1 — GROUP BY Status (unchanged)

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 558 | 63 | 71 |
| 2 | 558 | 62 | 62 |
| 3 | 558 | 32 | 51 |
| **Median** | **558** | **62** | **62** |

### Query C2 — GROUP BY Priority (unchanged)

| Run | Logical reads | CPU (ms) | Elapsed (ms) |
|---|---|---|---|
| 1 | 547 | 62 | 76 |
| 2 | 547 | 47 | 62 |
| 3 | 547 | 47 | 64 |
| **Median** | **547** | **47** | **64** |

---

## Before / After comparison (medians)

| Query | Step 2 — Baseline | Step 3 — Covering index | Logical reads Δ | Elapsed Δ |
|---|---|---|---|---|
| A-COUNT | 79 reads · 7 ms | 79 reads · 7 ms | 0 | 0 ms |
| **A Page 1** | **1,510 reads · 83 ms** | **5 reads · 0 ms** | **−1,505 (−99.7%)** | **−83 ms** |
| **B Page 500** | **1,510 reads · 83 ms** | **133 reads · 7 ms** | **−1,377 (−91.2%)** | **−76 ms** |
| C1 Status | 558 reads · 72 ms | 558 reads · 62 ms | 0 | −10 ms |
| C2 Priority | 547 reads · 62 ms | 547 reads · 64 ms | 0 | +2 ms |

---

## Actual execution plan findings

### A-COUNT (unchanged)
```
Index Seek(IX_Requests_Status_CreatedAt)
  SEEK: Status=@status AND CreatedAt >= @createdFrom AND CreatedAt < @upperBound
  → Stream Aggregate(COUNT(*)) → Compute Scalar
```
The optimizer still chose the **narrower** key-only index for the COUNT — sensible, since it has a smaller leaf row size and the COUNT needs no projected columns.

### Query A — page 1 (new plan)
```
Index Seek(IX_Requests_Status_CreatedAt_Covering_Exp)
  SEEK: Status=@status AND CreatedAt >= @createdFrom AND CreatedAt < @upperBound
  ORDERED FORWARD   [output: all 9 projected columns directly from leaf]
  → Top(OFFSET 0, FETCH 20)
```
**Key Lookups:** none  
**Sort operator:** **absent** — index key `(Status ASC, CreatedAt DESC, Id DESC)` exactly matches `ORDER BY CreatedAt DESC, Id DESC`; the seek output is already in the required order  
**Worktable:** none  
**Actual rows read:** 20 (seek stops as soon as 20 rows are emitted)

### Query B — page 500 (new plan)
```
Index Seek(IX_Requests_Status_CreatedAt_Covering_Exp)
  SEEK: Status=@status AND CreatedAt >= @createdFrom AND CreatedAt < @upperBound
  ORDERED FORWARD
  → Top(OFFSET 9980, FETCH 20)
```
**Key Lookups:** none  
**Sort operator:** **absent**  
**Worktable:** none  
**Actual rows read from index:** 10,000 (OFFSET 9,980 rows skipped + 20 returned)  
This is why page 500 costs 133 reads vs. 5 for page 1 — OFFSET pagination must traverse 9,980 leaf rows before delivering the 20 results. Still 91% fewer reads than the baseline 1,510.

### Summary queries C1 and C2 (unchanged)
Both continue to use `IX_Requests_Status_CreatedAt` and `IX_Requests_Priority_CreatedAt` respectively. The covering index is irrelevant to them.

---

## Cleanup verification

```sql
DROP INDEX IF EXISTS IX_Requests_Status_CreatedAt_Covering_Exp ON dbo.Requests;
```

Post-drop index count: **8** (identical to pre-experiment snapshot).  
All original indexes confirmed present and enabled (`is_disabled = 0`):

| Index | is_disabled |
|---|---|
| PK_Requests | 0 |
| IX_Requests_Status_CreatedAt | 0 |
| IX_Requests_Priority_CreatedAt | 0 |
| IX_Requests_AssignedTo_CreatedAt | 0 |
| IX_Requests_CreatedAt | 0 |
| IX_Requests_OrganizationName | 0 |
| PK_RequestStatusHistory | 0 |
| IX_RequestStatusHistory_RequestId_ChangedAt | 0 |

No `IX_Requests_Status_CreatedAt_Covering_Exp` present.

---

## Source files

| File | Description |
|---|---|
| `docs/t801_step3_measure.sql` | Measurement script (identical SQL to Step 2) |
| `docs/t801_step3_raw.txt` | Raw STATISTICS IO/TIME output, 3 runs × 5 queries |
| `docs/t801_step3_plans.txt` | Actual execution profiles for A-COUNT, Query A, Query B |
| `docs/t801_step3_results.md` | This summary |
