-- ============================================================
-- T-801 Step 3 — Covering Index Experiment: Measurements
-- Database  : RequestsManagement
-- Index     : IX_Requests_Status_CreatedAt_Covering_Exp
-- Filter    : Status=1 (InProgress), CreatedAt 2024-06-01..2025-06-30
-- Rows      : ~13,590 matching, 680 pages at pageSize=20
-- Deep page : 500 (OFFSET 9980)
-- Identical SQL and parameters to t801_step2_measure.sql
-- ============================================================

USE RequestsManagement;
GO

SET NOCOUNT ON;
SET STATISTICS IO  ON;
SET STATISTICS TIME ON;

DECLARE @status      tinyint   = CAST(1 AS tinyint);
DECLARE @createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z';
DECLARE @upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z';
DECLARE @offset_p1   int       = 0;
DECLARE @offset_p500 int       = 9980;
DECLARE @fetch       int       = 20;

-- ============================================================
-- QUERY A-COUNT  (SELECT COUNT with Status+date filters)
-- ============================================================
PRINT '===== A-COUNT Run 1 =====';
SELECT COUNT(*) AS FilteredCount
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound;

PRINT '===== A-COUNT Run 2 =====';
SELECT COUNT(*) AS FilteredCount
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound;

PRINT '===== A-COUNT Run 3 =====';
SELECT COUNT(*) AS FilteredCount
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound;

-- ============================================================
-- QUERY A  (page 1 — OFFSET 0 FETCH 20)
-- ============================================================
PRINT '===== QUERY-A Page 1 Run 1 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p1 ROWS FETCH NEXT @fetch ROWS ONLY;

PRINT '===== QUERY-A Page 1 Run 2 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p1 ROWS FETCH NEXT @fetch ROWS ONLY;

PRINT '===== QUERY-A Page 1 Run 3 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p1 ROWS FETCH NEXT @fetch ROWS ONLY;

-- ============================================================
-- QUERY B  (page 500 — OFFSET 9980 FETCH 20)
-- ============================================================
PRINT '===== QUERY-B Page 500 Run 1 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p500 ROWS FETCH NEXT @fetch ROWS ONLY;

PRINT '===== QUERY-B Page 500 Run 2 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p500 ROWS FETCH NEXT @fetch ROWS ONLY;

PRINT '===== QUERY-B Page 500 Run 3 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p500 ROWS FETCH NEXT @fetch ROWS ONLY;

-- ============================================================
-- QUERY C1  (summary — GROUP BY Status)
-- ============================================================
PRINT '===== QUERY-C1 Summary/Status Run 1 =====';
SELECT [r].[Status], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Status];

PRINT '===== QUERY-C1 Summary/Status Run 2 =====';
SELECT [r].[Status], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Status];

PRINT '===== QUERY-C1 Summary/Status Run 3 =====';
SELECT [r].[Status], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Status];

-- ============================================================
-- QUERY C2  (summary — GROUP BY Priority)
-- ============================================================
PRINT '===== QUERY-C2 Summary/Priority Run 1 =====';
SELECT [r].[Priority], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Priority];

PRINT '===== QUERY-C2 Summary/Priority Run 2 =====';
SELECT [r].[Priority], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Priority];

PRINT '===== QUERY-C2 Summary/Priority Run 3 =====';
SELECT [r].[Priority], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Priority];

SET STATISTICS IO  OFF;
SET STATISTICS TIME OFF;
