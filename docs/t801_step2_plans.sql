-- ============================================================
-- T-801 Step 2 — Actual Execution Plans (SET STATISTICS XML ON)
-- Note: STATISTICS XML ON causes actual plans to be returned as
-- an extra result set after each statement.  Combine with
-- STATISTICS IO/TIME to get I/O and timing in the same run.
-- ============================================================

USE RequestsManagement;
GO

SET NOCOUNT ON;
SET STATISTICS IO  ON;
SET STATISTICS TIME ON;
SET STATISTICS XML ON;  -- actual execution plan XML (not estimated)

DECLARE @status      tinyint   = CAST(1 AS tinyint);
DECLARE @createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z';
DECLARE @upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z';
DECLARE @offset_p1   int       = 0;
DECLARE @offset_p500 int       = 9980;
DECLARE @fetch       int       = 20;

-- A-COUNT
PRINT '===== PLAN: A-COUNT =====';
SELECT COUNT(*) AS FilteredCount
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound;
GO

USE RequestsManagement;
DECLARE @status      tinyint   = CAST(1 AS tinyint);
DECLARE @createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z';
DECLARE @upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z';
DECLARE @offset_p1   int       = 0;
DECLARE @fetch       int       = 20;

-- Query A page 1
PRINT '===== PLAN: QUERY-A Page 1 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p1 ROWS FETCH NEXT @fetch ROWS ONLY;
GO

USE RequestsManagement;
DECLARE @status      tinyint   = CAST(1 AS tinyint);
DECLARE @createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z';
DECLARE @upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z';
DECLARE @offset_p500 int       = 9980;
DECLARE @fetch       int       = 20;

-- Query B page 500
PRINT '===== PLAN: QUERY-B Page 500 =====';
SELECT [r].[Id], [r].[Title], [r].[OrganizationName], [r].[Status],
       [r].[Priority], [r].[AssignedTo], [r].[CreatedAt], [r].[UpdatedAt], [r].[RowVersion]
FROM   [Requests] AS [r]
WHERE  [r].[Status]    = @status
  AND  [r].[CreatedAt] >= @createdFrom
  AND  [r].[CreatedAt] <  @upperBound
ORDER BY [r].[CreatedAt] DESC, [r].[Id] DESC
OFFSET @offset_p500 ROWS FETCH NEXT @fetch ROWS ONLY;
GO

USE RequestsManagement;

-- Query C1 summary by Status
PRINT '===== PLAN: QUERY-C1 Summary/Status =====';
SELECT [r].[Status], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Status];
GO

USE RequestsManagement;

-- Query C2 summary by Priority
PRINT '===== PLAN: QUERY-C2 Summary/Priority =====';
SELECT [r].[Priority], COUNT(*) AS [Count]
FROM   [Requests] AS [r]
GROUP BY [r].[Priority];
GO

SET STATISTICS XML OFF;
SET STATISTICS IO  OFF;
SET STATISTICS TIME OFF;
