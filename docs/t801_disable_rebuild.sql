-- ============================================================
-- T-801 — DISABLE / REBUILD comparison for IX_Requests_Status_CreatedAt
-- Target: A-COUNT query (the one that uses this index in the baseline)
-- Finally-style guarantee: REBUILD always runs, even on measurement error.
-- ============================================================

USE RequestsManagement;
GO

SET NOCOUNT ON;

-- ── PRE-CHECK ────────────────────────────────────────────────────────────────
PRINT '=== PRE-CHECK: index state before DISABLE ===';
SELECT i.name, i.is_disabled
FROM sys.indexes i JOIN sys.objects o ON i.object_id = o.object_id
WHERE o.name = 'Requests' AND i.name = 'IX_Requests_Status_CreatedAt';

-- ── DISABLE ──────────────────────────────────────────────────────────────────
PRINT '=== DISABLING IX_Requests_Status_CreatedAt ===';
ALTER INDEX IX_Requests_Status_CreatedAt ON dbo.Requests DISABLE;
PRINT 'DISABLE succeeded.';

-- ── MEASUREMENTS (inside TRY so REBUILD runs even on failure) ────────────────
BEGIN TRY
    DECLARE @status      tinyint   = CAST(1 AS tinyint);
    DECLARE @createdFrom datetime2 = '2024-06-01T00:00:00.0000000Z';
    DECLARE @upperBound  datetime2 = '2025-07-01T00:00:00.0000000Z';

    PRINT '=== A-COUNT WITHOUT IX_Requests_Status_CreatedAt ===';
    SET STATISTICS IO ON;
    SET STATISTICS TIME ON;
    SET STATISTICS PROFILE ON;

    PRINT '--- Run 1 ---';
    SELECT COUNT(*) AS FilteredCount
    FROM [Requests] AS [r]
    WHERE [r].[Status]    = @status
      AND [r].[CreatedAt] >= @createdFrom
      AND [r].[CreatedAt] <  @upperBound;

    PRINT '--- Run 2 ---';
    SELECT COUNT(*) AS FilteredCount
    FROM [Requests] AS [r]
    WHERE [r].[Status]    = @status
      AND [r].[CreatedAt] >= @createdFrom
      AND [r].[CreatedAt] <  @upperBound;

    PRINT '--- Run 3 ---';
    SELECT COUNT(*) AS FilteredCount
    FROM [Requests] AS [r]
    WHERE [r].[Status]    = @status
      AND [r].[CreatedAt] >= @createdFrom
      AND [r].[CreatedAt] <  @upperBound;

    SET STATISTICS PROFILE OFF;
    SET STATISTICS IO OFF;
    SET STATISTICS TIME OFF;

END TRY
BEGIN CATCH
    PRINT '=== MEASUREMENT ERROR — proceeding to REBUILD ===';
    PRINT ERROR_MESSAGE();
END CATCH;

-- ── REBUILD (always executes — finally-style) ────────────────────────────────
PRINT '=== REBUILDING IX_Requests_Status_CreatedAt ===';
ALTER INDEX IX_Requests_Status_CreatedAt ON dbo.Requests REBUILD;
PRINT 'REBUILD succeeded.';

-- ── POST-CHECK: verify all indexes enabled ───────────────────────────────────
PRINT '=== POST-CHECK: all Requests indexes ===';
SELECT i.name, i.type_desc, i.is_disabled
FROM sys.indexes i JOIN sys.objects o ON i.object_id = o.object_id
WHERE o.name IN ('Requests','RequestStatusHistory') AND i.type > 0
ORDER BY o.name, i.name;
GO
