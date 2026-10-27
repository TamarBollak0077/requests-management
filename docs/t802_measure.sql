-- T-802 Covering Index Evaluation
-- Disposable test database: T802_Test
-- Safety: all operations on T802_Test only; never touches RequestsManagement schema

-- ============================================================
-- STEP 0: Create disposable test DB and copy data
-- ============================================================
USE master;
GO
IF DB_ID('T802_Test') IS NOT NULL
    DROP DATABASE T802_Test;
GO
CREATE DATABASE T802_Test;
GO
USE T802_Test;
GO

-- Mirror the Requests table structure
SELECT TOP 100000
    Id, Title, OrganizationName,
    CAST(Status AS tinyint)   AS Status,
    CAST(Priority AS tinyint) AS Priority,
    AssignedTo, CreatedAt, UpdatedAt,
    RowVersion
INTO dbo.Requests
FROM RequestsManagement.dbo.Requests
ORDER BY Id;
GO

-- Clustered index (PK) — mirrors production
ALTER TABLE dbo.Requests ADD CONSTRAINT PK_Requests PRIMARY KEY CLUSTERED (Id);
GO

-- Count rows and status distribution to confirm copy
SELECT COUNT(*) AS TotalRows FROM dbo.Requests;
SELECT Status, COUNT(*) AS Cnt FROM dbo.Requests GROUP BY Status;
GO

-- ============================================================
-- SECTION A: Baseline — no non-clustered indexes at all
-- Confirms the clustered scan penalty
-- ============================================================
PRINT '=== SECTION A: No NC indexes (pure clustered scan baseline) ===';
GO
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

-- A-COUNT no index
SELECT COUNT(*)
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000';
GO

-- A PAGE 1 no index
SELECT Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000'
ORDER BY CreatedAt DESC, Id DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

-- ============================================================
-- SECTION B: Covering index ONLY (no key-only index)
-- Simulates DROP_EXISTING=ON replacement
-- ============================================================
PRINT '=== SECTION B: Covering index only (key-only removed) ===';
GO

CREATE NONCLUSTERED INDEX IX_Requests_Status_CreatedAt_Covering
ON dbo.Requests (Status ASC, CreatedAt DESC, Id DESC)
INCLUDE (Title, OrganizationName, Priority, AssignedTo, UpdatedAt, RowVersion);
GO

-- Also add Priority_CreatedAt (unchanged) for GROUP BY Priority
CREATE NONCLUSTERED INDEX IX_Requests_Priority_CreatedAt
ON dbo.Requests (Priority ASC, CreatedAt DESC);
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '-- B1: A-COUNT with covering index only';
SELECT COUNT(*)
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000';
GO

PRINT '-- B2: Query A page 1 with covering index';
SELECT Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000'
ORDER BY CreatedAt DESC, Id DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
GO

PRINT '-- B3: Query B page 500 with covering index';
SELECT Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000'
ORDER BY CreatedAt DESC, Id DESC
OFFSET 9980 ROWS FETCH NEXT 20 ROWS ONLY;
GO

PRINT '-- B4: C1 GROUP BY Status with covering index only';
SELECT Status, COUNT(*) AS [Count]
FROM dbo.Requests
GROUP BY Status;
GO

PRINT '-- B5: C2 GROUP BY Priority with covering index only';
SELECT Priority, COUNT(*) AS [Count]
FROM dbo.Requests
GROUP BY Priority;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

-- ============================================================
-- SECTION C: Both indexes present (covering + key-only)
-- Simulates ADDING covering index alongside existing one
-- ============================================================
PRINT '=== SECTION C: Both covering and key-only indexes ===';
GO

-- Add the key-only index (same as production IX_Requests_Status_CreatedAt)
CREATE NONCLUSTERED INDEX IX_Requests_Status_CreatedAt_KeyOnly
ON dbo.Requests (Status ASC, CreatedAt DESC);
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '-- C1: A-COUNT with both indexes (optimizer chooses)';
SELECT COUNT(*)
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000';
GO

PRINT '-- C2: Query A page 1 with both indexes';
SELECT Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000'
ORDER BY CreatedAt DESC, Id DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
GO

PRINT '-- C3: Query B page 500 with both indexes';
SELECT Id, Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt, RowVersion
FROM dbo.Requests
WHERE Status    = CAST(1 AS tinyint)
  AND CreatedAt >= '2024-06-01T00:00:00.0000000'
  AND CreatedAt <  '2025-07-01T00:00:00.0000000'
ORDER BY CreatedAt DESC, Id DESC
OFFSET 9980 ROWS FETCH NEXT 20 ROWS ONLY;
GO

PRINT '-- C4: GROUP BY Status with both indexes';
SELECT Status, COUNT(*) AS [Count]
FROM dbo.Requests
GROUP BY Status;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

-- ============================================================
-- SECTION D: Index sizes
-- ============================================================
PRINT '=== SECTION D: Index size comparison ===';
GO

SELECT
    i.name                                                      AS index_name,
    SUM(a.total_pages) * 8                                      AS total_kb,
    SUM(a.used_pages) * 8                                       AS used_kb,
    SUM(a.total_pages)                                          AS total_pages,
    i.type_desc
FROM sys.indexes i
JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE OBJECT_NAME(i.object_id) = 'Requests'
GROUP BY i.name, i.type_desc
ORDER BY total_kb DESC;
GO

-- ============================================================
-- SECTION E: Write overhead — INSERT benchmark
-- Compare 1000 inserts with covering-only vs with both indexes
-- ============================================================
PRINT '=== SECTION E: Write overhead (INSERT 1000 rows) ===';
GO

-- Remove key-only for first test (covering only)
DROP INDEX IF EXISTS IX_Requests_Status_CreatedAt_KeyOnly ON dbo.Requests;
GO

DECLARE @start datetime2 = SYSDATETIME();
DECLARE @i int = 1;
DECLARE @maxId int;
SELECT @maxId = MAX(Id) FROM dbo.Requests;

WHILE @i <= 1000
BEGIN
    INSERT INTO dbo.Requests (Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt)
    VALUES (
        CONCAT('T802InsertTest_', @maxId + @i),
        CONCAT('Org_', @i),
        CAST((@i % 4) AS tinyint),
        CAST((@i % 3) AS tinyint),
        CONCAT('user_', @i % 50),
        DATEADD(second, @i, '2025-01-01'),
        DATEADD(second, @i, '2025-01-01')
    );
    SET @i = @i + 1;
END;

DECLARE @end datetime2 = SYSDATETIME();
PRINT CONCAT('Covering-only INSERT 1000 rows: ',
    DATEDIFF(millisecond, @start, @end), ' ms');
GO

-- Delete test rows
DELETE FROM dbo.Requests WHERE Title LIKE 'T802InsertTest_%';
GO

-- Add key-only back for second INSERT test (both indexes)
CREATE NONCLUSTERED INDEX IX_Requests_Status_CreatedAt_KeyOnly
ON dbo.Requests (Status ASC, CreatedAt DESC);
GO

DECLARE @start2 datetime2 = SYSDATETIME();
DECLARE @j int = 1;
DECLARE @maxId2 int;
SELECT @maxId2 = MAX(Id) FROM dbo.Requests;

WHILE @j <= 1000
BEGIN
    INSERT INTO dbo.Requests (Title, OrganizationName, Status, Priority, AssignedTo, CreatedAt, UpdatedAt)
    VALUES (
        CONCAT('T802InsertTest_', @maxId2 + @j),
        CONCAT('Org_', @j),
        CAST((@j % 4) AS tinyint),
        CAST((@j % 3) AS tinyint),
        CONCAT('user_', @j % 50),
        DATEADD(second, @j, '2025-01-01'),
        DATEADD(second, @j, '2025-01-01')
    );
    SET @j = @j + 1;
END;

DECLARE @end2 datetime2 = SYSDATETIME();
PRINT CONCAT('Covering + key-only INSERT 1000 rows: ',
    DATEDIFF(millisecond, @start2, @end2), ' ms');
GO

-- Delete test rows
DELETE FROM dbo.Requests WHERE Title LIKE 'T802InsertTest_%';
GO

-- ============================================================
-- SECTION F: Write overhead — UPDATE benchmark
-- ============================================================
PRINT '=== SECTION F: Write overhead (UPDATE 1000 rows) ===';
GO

-- Grab 1000 Ids to update
DECLARE @ids TABLE (Id int);
INSERT INTO @ids SELECT TOP 1000 Id FROM dbo.Requests ORDER BY NEWID();

-- With both indexes
DECLARE @start3 datetime2 = SYSDATETIME();
UPDATE dbo.Requests
SET UpdatedAt = SYSDATETIME()
WHERE Id IN (SELECT Id FROM @ids);
DECLARE @end3 datetime2 = SYSDATETIME();
PRINT CONCAT('UPDATE 1000 rows (covering + key-only): ',
    DATEDIFF(millisecond, @start3, @end3), ' ms');
GO

-- Drop key-only and repeat
DROP INDEX IF EXISTS IX_Requests_Status_CreatedAt_KeyOnly ON dbo.Requests;
GO

DECLARE @ids2 TABLE (Id int);
INSERT INTO @ids2 SELECT TOP 1000 Id FROM dbo.Requests ORDER BY NEWID();

DECLARE @start4 datetime2 = SYSDATETIME();
UPDATE dbo.Requests
SET UpdatedAt = SYSDATETIME()
WHERE Id IN (SELECT Id FROM @ids2);
DECLARE @end4 datetime2 = SYSDATETIME();
PRINT CONCAT('UPDATE 1000 rows (covering-only): ',
    DATEDIFF(millisecond, @start4, @end4), ' ms');
GO

-- ============================================================
-- CLEANUP: Drop test database
-- ============================================================
USE master;
GO
DROP DATABASE IF EXISTS T802_Test;
GO
PRINT '=== T802_Test database dropped. Cleanup complete. ===';
GO
