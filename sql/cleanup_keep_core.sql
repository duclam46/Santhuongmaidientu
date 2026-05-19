USE [DOAN_BANHANG];
GO

SET NOCOUNT ON;

-- Safety: list of table names to keep (case-sensitive according to your DB)
DECLARE @Keep TABLE (name SYSNAME);
INSERT INTO @Keep (name) VALUES ('Colors'), ('Sizes'), ('Categories');

-- Disable all FK constraints temporarily
EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';
GO

DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + 'DELETE FROM ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + ';' + CHAR(13)
FROM sys.tables t
WHERE t.is_ms_shipped = 0
  AND t.name NOT IN ('Colors','Sizes','Categories','__EFMigrationsHistory');

IF LEN(@sql) = 0
BEGIN
    PRINT 'No tables to delete.';
END
ELSE
BEGIN
    PRINT 'Executing deletes...';
    EXEC sp_executesql @sql;
    PRINT 'Deletes complete.';
END

-- Re-enable constraints (and check data)
EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';
GO

PRINT 'Cleanup finished. Kept tables: Colors, Sizes, Categories, and __EFMigrationsHistory.';
