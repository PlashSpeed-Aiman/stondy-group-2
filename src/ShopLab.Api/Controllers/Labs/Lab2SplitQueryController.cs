using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Domain;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 2 — Cartesian explosion vs split queries.
/// Loading a product with 3 collections in ONE query returns
/// reviews × orderItems × tags rows per product. AsSplitQuery() sends one query per collection instead.
/// </summary>
[Route("api/labs/2-split-query")]
public sealed class Lab2SplitQueryController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 2 - Single vs split queries";

    /// <param name="take">How many products to load.</param>
    /// <param name="hot">true = pick the most-ordered products (much bigger explosion — careful!).</param>
    /// <param name="categoryId">Optional category filter.</param>
    [HttpGet("single")]
    public Task<IActionResult> Single(int take = 3, bool hot = false, int? categoryId = null)
    {
        var query = GraphQuery(take, hot, categoryId).AsSingleQuery().TagWith("Lab 2 single query");
        return MeasureAsync("single query (JOINs)", async ct => Map(await query.ToListAsync(ct)), explain: query,
            notes: "One command, but rows returned = sum of cartesianRowsInSingleQuery. EF also logs MultipleCollectionIncludeWarning.");
    }

    [HttpGet("split")]
    public Task<IActionResult> Split(int take = 3, bool hot = false, int? categoryId = null)
    {
        var query = GraphQuery(take, hot, categoryId).AsSplitQuery().TagWith("Lab 2 split query");
        return MeasureAsync("split query", async ct => Map(await query.ToListAsync(ct)), explain: query,
            notes: "One command per Include'd collection; each repeats the parent filter/ORDER BY. Trade-off: more round trips, no transactional consistency between them.");
    }

    /// <summary>Filtered include: only load the 5 latest reviews per product.</summary>
    [HttpGet("filtered-include")]
    public Task<IActionResult> FilteredInclude(int take = 3, bool hot = false)
    {
        var products = Db.Products.TagWith("Lab 2 filtered include").AsNoTracking();
        products = hot ? products.OrderBy(p => p.Id) : products.OrderByDescending(p => p.Id);

        var query = products
            .Take(take)
            .Include(p => p.Reviews.OrderByDescending(r => r.CreatedAt).Take(5))
            .Include(p => p.ProductTags).ThenInclude(pt => pt.Tag)
            .AsSplitQuery();

        return MeasureAsync("filtered include + split", async ct => Map(await query.ToListAsync(ct)), explain: query,
            notes: "Often the real fix is not split vs single — it's not loading 2,000 rows you never show.");
    }

    private IQueryable<Product> GraphQuery(int take, bool hot, int? categoryId)
    {
        var products = Db.Products.AsNoTracking();
        if (categoryId is not null)
            products = products.Where(p => p.CategoryId == categoryId);

        // Low ids are the "hot" products in the seeded data.
        products = hot ? products.OrderBy(p => p.Id) : products.OrderByDescending(p => p.Id);

        return products
            .Take(take)
            .Include(p => p.Reviews)
            .Include(p => p.OrderItems)
            .Include(p => p.ProductTags).ThenInclude(pt => pt.Tag);
    }

    private static List<ProductGraphDto> Map(List<Product> products) => products
        .Select(p => new ProductGraphDto(
            p.Id,
            p.Name,
            p.Reviews.Count,
            p.OrderItems.Count,
            p.ProductTags.Select(t => t.Tag.Name).ToList(),
            (long)Math.Max(1, p.Reviews.Count) * Math.Max(1, p.OrderItems.Count) * Math.Max(1, p.ProductTags.Count)))
        .ToList();
}
