using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 4 — Change tracking cost.
/// The SQL is identical for the first three variants: the difference is purely
/// .NET-side work (snapshotting, identity map). Compare elapsedMs - sqlDurationMs.
/// </summary>
[Route("api/labs/4-tracking")]
public sealed class Lab4TrackingController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 4 - Tracking vs no-tracking";

    [HttpGet("tracked")]
    public Task<IActionResult> Tracked(int take = 20_000)
    {
        var query = Db.Orders.TagWith("Lab 4 tracked").Include(o => o.Customer).OrderBy(o => o.Id).Take(take);
        return MeasureAsync("tracked (default)", ct => query.ToListAsync(ct), explain: query,
            notes: "trackedEntities = orders + distinct customers. Each one has a snapshot kept for DetectChanges.");
    }

    [HttpGet("no-tracking")]
    public Task<IActionResult> NoTracking(int take = 20_000)
    {
        var query = Db.Orders.TagWith("Lab 4 no tracking").AsNoTracking().Include(o => o.Customer).OrderBy(o => o.Id).Take(take);
        return MeasureAsync("AsNoTracking", ct => query.ToListAsync(ct), explain: query,
            notes: "No identity resolution: the same customer is materialised as a separate object for every order.");
    }

    [HttpGet("no-tracking-identity-resolution")]
    public Task<IActionResult> NoTrackingWithIdentityResolution(int take = 20_000)
    {
        var query = Db.Orders.TagWith("Lab 4 no tracking + identity resolution")
            .AsNoTrackingWithIdentityResolution().Include(o => o.Customer).OrderBy(o => o.Id).Take(take);
        return MeasureAsync("AsNoTrackingWithIdentityResolution", ct => query.ToListAsync(ct), explain: query,
            notes: "Not tracked, but one Customer instance per key (useful for big graphs with repeated references).");
    }

    [HttpGet("projection")]
    public Task<IActionResult> Projection(int take = 20_000)
    {
        var query = Db.Orders.TagWith("Lab 4 projection").OrderBy(o => o.Id).Take(take)
            .Select(o => new OrderRowDto(o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId));
        return MeasureAsync("Select to DTO", ct => query.ToListAsync(ct), explain: query,
            notes: "Fewer columns AND no tracking. Usually the fastest option for read-only endpoints.");
    }
}
