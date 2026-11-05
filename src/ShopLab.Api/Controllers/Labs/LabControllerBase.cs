using System.Collections;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// Shared plumbing for lab endpoints:
///  - times the variant and counts the SQL commands it sent
///  - returns the first SQL statements in the response body
///  - add <c>?explain=true</c> to any lab endpoint to get the SQL from
///    <c>ToQueryString()</c> WITHOUT executing it
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class LabControllerBase(ShopDbContext db, SqlCapture sqlCapture) : ControllerBase
{
    private const int MaxSampleRows = 5;
    private const int MaxSqlInResponse = 3;

    protected ShopDbContext Db { get; } = db;

    protected abstract string LabName { get; }

    private bool ExplainOnly =>
        Request.Query.TryGetValue("explain", out var v) && bool.TryParse(v, out var b) && b;

    protected async Task<IActionResult> MeasureAsync<T>(
        string variant,
        Func<CancellationToken, Task<T>> work,
        IQueryable? explain = null,
        string? notes = null)
    {
        if (ExplainOnly)
        {
            return explain is null
                ? BadRequest(new { error = "This variant has no single query to explain. Run it and check firstSqlCommands." })
                : Ok(new { lab = LabName, variant, sql = explain.ToQueryString(), notes });
        }

        var before = sqlCapture.Count;
        var sw = Stopwatch.StartNew();
        var result = await work(HttpContext.RequestAborted);
        sw.Stop();

        var commands = sqlCapture.Snapshot().Skip(before).ToList();

        int? rows = result is ICollection c ? c.Count : null;
        object? body = result is IEnumerable e and not string
            ? e.Cast<object>().Take(MaxSampleRows).ToList()
            : result;

        return Ok(new LabResult(
            LabName,
            variant,
            Math.Round(sw.Elapsed.TotalMilliseconds, 1),
            commands.Count,
            Math.Round(commands.Sum(x => x.DurationMs), 1),
            rows,
            Db.ChangeTracker.Entries().Count(),
            notes,
            commands.Take(MaxSqlInResponse).Select(x => x.Sql).ToList(),
            body));
    }
}
