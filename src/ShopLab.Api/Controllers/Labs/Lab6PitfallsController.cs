using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 6 — Common pitfalls: client-side evaluation, Count vs Any, and bulk updates.
/// </summary>
[Route("api/labs/6-pitfalls")]
public sealed class Lab6PitfallsController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 6 - Pitfalls";

    /// <summary>Loads EVERY order into memory, then counts in C#.</summary>
    [HttpGet("count-client")]
    public Task<IActionResult> CountClient(decimal minTotal = 1000)
        => MeasureAsync("ToListAsync() then Count() in memory", async ct =>
        {
            var all = await Db.Orders.TagWith("Lab 6 count client-side").AsNoTracking().ToListAsync(ct);
            return all.Count(o => o.Total > minTotal);
        }, explain: Db.Orders.AsNoTracking(),
        notes: "The WHERE never reaches SQL. Same thing happens with AsEnumerable() or calling a C# method EF can't translate.");

    [HttpGet("count-server")]
    public Task<IActionResult> CountServer(decimal minTotal = 1000)
    {
        var query = Db.Orders.TagWith("Lab 6 count server-side").Where(o => o.Total > minTotal);
        return MeasureAsync("CountAsync(predicate)", ct => query.CountAsync(ct), explain: query,
            notes: "SELECT COUNT(*) ... WHERE — one number comes back.");
    }

    [HttpGet("exists-count")]
    public Task<IActionResult> ExistsWithCount(int customerId = 1)
    {
        var query = Db.Orders.TagWith("Lab 6 exists via Count").Where(o => o.CustomerId == customerId);
        return MeasureAsync("CountAsync() > 0", async ct => await query.CountAsync(ct) > 0, explain: query,
            notes: "Counts every matching row just to answer yes/no. Customer 1 is the busiest in the seed data.");
    }

    [HttpGet("exists-any")]
    public Task<IActionResult> ExistsWithAny(int customerId = 1)
    {
        var query = Db.Orders.TagWith("Lab 6 exists via Any").Where(o => o.CustomerId == customerId);
        return MeasureAsync("AnyAsync()", ct => query.AnyAsync(ct), explain: query,
            notes: "EXISTS / TOP(1): stops at the first match.");
    }

    /// <summary>Load, modify, SaveChanges — EF batches the UPDATEs, but every row travels to .NET and back.</summary>
    [HttpPost("vip-tracked")]
    public async Task<IActionResult> MarkVipsTracked(int minOrders = 20)
    {
        await ResetVipsAsync();
        var query = Db.Customers.TagWith("Lab 6 VIP tracked").Where(c => c.Orders.Count >= minOrders);

        return await MeasureAsync("load + modify + SaveChanges", async ct =>
        {
            var vips = await query.ToListAsync(ct);
            foreach (var c in vips) c.IsVip = true;
            await Db.SaveChangesAsync(ct);
            return vips.Count;
        }, explain: query,
        notes: "Count the commands: 1 SELECT + ceil(n / MaxBatchSize) batched UPDATEs. Try UseSqlServer(o => o.MaxBatchSize(1)).");
    }

    /// <summary>Single set-based UPDATE executed by the database.</summary>
    [HttpPost("vip-execute-update")]
    public async Task<IActionResult> MarkVipsExecuteUpdate(int minOrders = 20)
    {
        await ResetVipsAsync();
        var query = Db.Customers.TagWith("Lab 6 VIP ExecuteUpdate").Where(c => c.Orders.Count >= minOrders);

        return await MeasureAsync("ExecuteUpdateAsync",
            ct => query.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsVip, true), ct),
            explain: query,
            notes: "One UPDATE ... WHERE (subquery). Bypasses the change tracker — tracked entities are NOT refreshed.");
    }

    private Task<int> ResetVipsAsync() =>
        Db.Customers.Where(c => c.IsVip).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsVip, false));
}
