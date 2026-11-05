using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 5 — Offset vs keyset (seek) pagination.
/// Try offset with page=1, then page=5000. Keyset cost stays flat however deep you go.
/// Index 3.4 (OrderDate, Id) is what makes keyset pagination fast.
/// </summary>
[Route("api/labs/5-pagination")]
public sealed class Lab5PaginationController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 5 - Offset vs keyset pagination";

    [HttpGet("offset")]
    public Task<IActionResult> Offset(int page = 1, int pageSize = 20, bool includeTotal = false)
    {
        var ordered = Db.Orders.TagWith("Lab 5 offset").AsNoTracking()
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id);
        var query = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderRowDto(o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId));

        return MeasureAsync($"OFFSET page {page}", async ct =>
        {
            var items = await query.ToListAsync(ct);
            int? total = includeTotal ? await Db.Orders.TagWith("Lab 5 total count").CountAsync(ct) : null;
            return new { items, page, pageSize, total };
        }, explain: query,
        notes: "SQL Server still reads and discards (page-1)*pageSize rows. includeTotal=true adds a full COUNT(*) — often the most expensive part.");
    }

    /// <param name="cursor">nextCursor from the previous response (omit for the first page).</param>
    [HttpGet("keyset")]
    public Task<IActionResult> Keyset(string? cursor = null, int pageSize = 20)
    {
        var orders = Db.Orders.TagWith("Lab 5 keyset").AsNoTracking();

        if (TryParseCursor(cursor, out var lastDate, out var lastId))
        {
            orders = orders.Where(o => o.OrderDate < lastDate || (o.OrderDate == lastDate && o.Id < lastId));
        }

        var query = orders
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id)
            .Take(pageSize)
            .Select(o => new OrderRowDto(o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId));

        return MeasureAsync("keyset (seek)", async ct =>
        {
            var items = await query.ToListAsync(ct);
            var next = items.Count == pageSize ? MakeCursor(items[^1].OrderDate, items[^1].Id) : null;
            return new KeysetPage<OrderRowDto>(items, next);
        }, explain: query,
        notes: "WHERE (OrderDate, Id) < (@lastDate, @lastId). Cannot jump to page N — that's the trade-off.");
    }

    private static string MakeCursor(DateTime date, int id) =>
        $"{date.Ticks.ToString(CultureInfo.InvariantCulture)}_{id.ToString(CultureInfo.InvariantCulture)}";

    private static bool TryParseCursor(string? cursor, out DateTime date, out int id)
    {
        date = default;
        id = default;
        var parts = cursor?.Split('_');
        if (parts is not { Length: 2 }
            || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out id))
            return false;

        date = new DateTime(ticks);
        return true;
    }
}
