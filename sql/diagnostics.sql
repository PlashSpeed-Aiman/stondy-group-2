/*  ShopLab — SQL Server diagnostics toolbox
    Run in SSMS or VS Code (mssql extension) against the ShopLab database.
    Tip (SSMS): Ctrl+M = "Include Actual Execution Plan" before running a query.
*/
USE ShopLab;
GO

/* ------------------------------------------------------------------
   1. Measure a query copied from the EF Core log / ?explain=true
      Paste the SQL (with DECLAREs for parameters) between the SET lines.
      Read the Messages tab: "logical reads" per table is THE number to compare.
   ------------------------------------------------------------------ */
SET STATISTICS IO ON;
SET STATISTICS TIME ON;

DECLARE @email nvarchar(256) = N'paste.email.here@example.com';
SELECT [c].[Id], [c].[Email]
FROM [Customers] AS [c]
WHERE [c].[Email] = @email;

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

/* ------------------------------------------------------------------
   2. Find EF Core queries in the plan cache by their TagWith() comment
   ------------------------------------------------------------------ */
SELECT TOP (20)
       qs.execution_count,
       qs.total_logical_reads / qs.execution_count AS avg_logical_reads,
       qs.total_elapsed_time / qs.execution_count / 1000.0 AS avg_elapsed_ms,
       SUBSTRING(st.text, 1, 400) AS query_text,
       qp.query_plan
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
WHERE st.text LIKE N'%-- Lab %'
ORDER BY qs.last_execution_time DESC;
GO

/* ------------------------------------------------------------------
   3. Indexes on a table + their size
   ------------------------------------------------------------------ */
SELECT i.name AS index_name, i.type_desc, i.is_unique, i.filter_definition,
       SUM(ps.used_page_count) * 8 / 1024.0 AS size_mb
FROM sys.indexes i
JOIN sys.dm_db_partition_stats ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE i.object_id = OBJECT_ID(N'dbo.Orders')
GROUP BY i.name, i.type_desc, i.is_unique, i.filter_definition;
GO

/* ------------------------------------------------------------------
   4. Statistics: how does the optimizer think the data is distributed?
   ------------------------------------------------------------------ */
-- DBCC SHOW_STATISTICS ('dbo.Orders', 'IX_Orders_CustomerId');
SELECT Status, COUNT(*) AS orders FROM dbo.Orders GROUP BY Status ORDER BY orders DESC;
GO

/* ------------------------------------------------------------------
   5. Query Store (on by default in SQL Server 2022 for new databases)
      SSMS: Database > Query Store > "Top Resource Consuming Queries"
   ------------------------------------------------------------------ */
SELECT TOP (20)
       q.query_id,
       rs.count_executions,
       rs.avg_duration / 1000.0 AS avg_ms,
       rs.avg_logical_io_reads,
       SUBSTRING(qt.query_sql_text, 1, 300) AS query_text
FROM sys.query_store_query q
JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
JOIN sys.query_store_plan p ON p.query_id = q.query_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
ORDER BY rs.avg_logical_io_reads DESC;
GO

/* ------------------------------------------------------------------
   6. DEV ONLY: cold cache (same as POST /api/diagnostics/cold-cache)
   ------------------------------------------------------------------ */
-- CHECKPOINT; DBCC DROPCLEANBUFFERS; DBCC FREEPROCCACHE;
