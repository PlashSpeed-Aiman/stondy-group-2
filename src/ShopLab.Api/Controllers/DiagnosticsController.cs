using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;

namespace ShopLab.Api.Controllers;

/// <summary>Helpers for looking at what EF Core and SQL Server are doing.</summary>
[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(ShopDbContext db, RecentRequestStore store, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>The last N API requests and every SQL command (with parameters and duration) they ran.</summary>
    [HttpGet("requests")]
    public IReadOnlyList<RequestSqlLog> Requests() => store.Latest();

    [HttpDelete("requests")]
    public IActionResult ClearRequests()
    {
        store.Clear();
        return NoContent();
    }

    /// <summary>All indexes with how often they were seeked / scanned / updated since SQL Server started.</summary>
    [HttpGet("indexes")]
    public async Task<IReadOnlyList<IndexUsageRow>> Indexes(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<IndexUsageRow>("""
            SELECT t.name AS TableName,
                   i.name AS IndexName,
                   i.type_desc AS IndexType,
                   STUFF((SELECT ', ' + c.name + CASE WHEN ic.is_included_column = 1 THEN ' (incl)' ELSE '' END
                          FROM sys.index_columns ic
                          JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                          WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                          ORDER BY ic.is_included_column, ic.key_ordinal
                          FOR XML PATH('')), 1, 2, '') AS Columns,
                   ISNULL(s.user_seeks, 0)   AS UserSeeks,
                   ISNULL(s.user_scans, 0)   AS UserScans,
                   ISNULL(s.user_lookups, 0) AS UserLookups,
                   ISNULL(s.user_updates, 0) AS UserUpdates
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            LEFT JOIN sys.dm_db_index_usage_stats s
                   ON s.object_id = i.object_id AND s.index_id = i.index_id AND s.database_id = DB_ID()
            WHERE i.name IS NOT NULL AND t.is_ms_shipped = 0
            ORDER BY t.name, i.index_id
            """).ToListAsync(ct);

    /// <summary>What SQL Server's optimizer wished it had. Treat as hints, not orders.</summary>
    [HttpGet("missing-indexes")]
    public async Task<IReadOnlyList<MissingIndexRow>> MissingIndexes(CancellationToken ct) =>
        await db.Database.SqlQueryRaw<MissingIndexRow>("""
            SELECT TOP (25)
                   mid.statement AS TableName,
                   mid.equality_columns AS EqualityColumns,
                   mid.inequality_columns AS InequalityColumns,
                   mid.included_columns AS IncludedColumns,
                   migs.user_seeks AS UserSeeks,
                   migs.avg_user_impact AS AvgUserImpactPercent
            FROM sys.dm_db_missing_index_group_stats migs
            JOIN sys.dm_db_missing_index_groups mig ON migs.group_handle = mig.index_group_handle
            JOIN sys.dm_db_missing_index_details mid ON mig.index_handle = mid.index_handle
            WHERE mid.database_id = DB_ID()
            ORDER BY migs.avg_user_impact * migs.user_seeks DESC
            """).ToListAsync(ct);

    /// <summary>
    /// DEV ONLY: flush the buffer pool and plan cache so the next run is a "cold" run
    /// (reads from disk + recompiles). Never do this on a shared/production server.
    /// </summary>
    [HttpPost("cold-cache")]
    public async Task<IActionResult> ColdCache(CancellationToken ct)
    {
        if (!env.IsDevelopment()) return Forbid();
        await db.Database.ExecuteSqlRawAsync("CHECKPOINT; DBCC DROPCLEANBUFFERS; DBCC FREEPROCCACHE;", ct);
        return NoContent();
    }
}

public sealed class IndexUsageRow
{
    public string TableName { get; set; } = "";
    public string IndexName { get; set; } = "";
    public string IndexType { get; set; } = "";
    public string? Columns { get; set; }
    public long UserSeeks { get; set; }
    public long UserScans { get; set; }
    public long UserLookups { get; set; }
    public long UserUpdates { get; set; }
}

public sealed class MissingIndexRow
{
    public string? TableName { get; set; }
    public string? EqualityColumns { get; set; }
    public string? InequalityColumns { get; set; }
    public string? IncludedColumns { get; set; }
    public long UserSeeks { get; set; }
    public double AvgUserImpactPercent { get; set; }
}
