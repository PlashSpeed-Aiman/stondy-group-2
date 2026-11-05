using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Domain;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 3 — Indexes.
/// Each endpoint is a query shape that needs a different kind of index.
/// Run it, look at the actual execution plan + logical reads, then add the
/// matching index in ShopDbContext.ConfigureLabIndexes and run it again.
/// </summary>
[Route("api/labs/3-indexes")]
public sealed class Lab3IndexesController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 3 - Indexes";

    /// <summary>Real values from the seeded data to plug into the other endpoints.</summary>
    [HttpGet("sample-values")]
    public async Task<IActionResult> SampleValues(CancellationToken ct)
    {
        var customer = await Db.Customers.AsNoTracking().OrderBy(c => c.Id).Skip(12_345).Select(c => new { c.Email, c.Country, c.City }).FirstOrDefaultAsync(ct);
        return Ok(new { customer, statuses = Enum.GetNames<OrderStatus>(), productSearchTerms = new[] { "Small", "Steel", "Chair" } });
    }

    /// <summary>3.1 Point lookup on a non-key column.</summary>
    [HttpGet("customer-by-email")]
    public async Task<IActionResult> CustomerByEmail(string? email = null)
    {
        email ??= await Db.Customers.Where(c => c.Id == 12_345).Select(c => c.Email).FirstOrDefaultAsync() ?? "";

        var query = Db.Customers.TagWith("Lab 3.1 customer by email").AsNoTracking().Where(c => c.Email == email);
        return await MeasureAsync("WHERE Email = @email", ct => query.ToListAsync(ct), explain: query,
            notes: "Without an index: Clustered Index Scan of every customer. Index 3.1 turns it into an Index Seek (+ Key Lookup — why?).");
    }

    /// <summary>3.2 Composite index — does column order matter? Try with only country, then with country+city.</summary>
    [HttpGet("customers-by-location")]
    public Task<IActionResult> CustomersByLocation(string country = "Malaysia", string? city = "Penang")
    {
        var query = Db.Customers.TagWith("Lab 3.2 customers by location").AsNoTracking().Where(c => c.Country == country);
        if (city is not null) query = query.Where(c => c.City == city);
        var projected = query.Select(c => new { c.Id, c.Email, c.City });

        return MeasureAsync("WHERE Country [AND City]", ct => projected.CountAsync(ct), explain: projected,
            notes: "Index (Country, City) helps both 'country only' and 'country + city'. Would (City, Country) help 'country only'?");
    }

    /// <summary>3.3 Equality + range + sort + covering.</summary>
    [HttpGet("orders-by-status")]
    public Task<IActionResult> OrdersByStatus(OrderStatus status = OrderStatus.Pending, int days = 30, int take = 100)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var query = Db.Orders
            .TagWith("Lab 3.3 orders by status and date")
            .Where(o => o.Status == status && o.OrderDate >= since)
            .OrderByDescending(o => o.OrderDate)
            .Take(take)
            .Select(o => new OrderRowDto(o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId));

        return MeasureAsync("WHERE Status = @s AND OrderDate >= @d ORDER BY OrderDate DESC", ct => query.ToListAsync(ct),
            explain: query,
            notes: "Equality column first, range column second. INCLUDE(CustomerId, Total) makes it covering: no Key Lookup, no Sort.");
    }

    /// <summary>3.4 Sargable vs non-sargable predicates.</summary>
    /// <param name="mode">"nonsargable" (OrderDate.Year == year) or "sargable" (date range).</param>
    [HttpGet("orders-in-year")]
    public Task<IActionResult> OrdersInYear(int year = 2025, string mode = "nonsargable")
    {
        IQueryable<Order> query = Db.Orders.TagWith($"Lab 3.4 orders in year ({mode})");
        if (mode.Equals("sargable", StringComparison.OrdinalIgnoreCase))
        {
            var from = new DateTime(year, 1, 1);
            var to = from.AddYears(1);
            query = query.Where(o => o.OrderDate >= from && o.OrderDate < to);
        }
        else
        {
            query = query.Where(o => o.OrderDate.Year == year); // -> DATEPART(year, [o].[OrderDate]) = @year
        }

        return MeasureAsync(mode, ct => query.CountAsync(ct), explain: query.Select(o => o.Id),
            notes: "With index 3.4 in place, only the sargable version can SEEK. A function on the column forces a scan.");
    }

    /// <summary>3.5 LIKE 'x%' vs LIKE '%x%'.</summary>
    /// <param name="mode">"startswith" | "contains" | "endswith"</param>
    [HttpGet("product-search")]
    public Task<IActionResult> ProductSearch(string term = "Small", string mode = "startswith", int take = 50)
    {
        var products = Db.Products.TagWith($"Lab 3.5 product search ({mode})").AsNoTracking();
        products = mode.ToLowerInvariant() switch
        {
            "contains" => products.Where(p => p.Name.Contains(term)),
            "endswith" => products.Where(p => p.Name.EndsWith(term)),
            _ => products.Where(p => p.Name.StartsWith(term))
        };
        var query = products.OrderBy(p => p.Name).Take(take).Select(p => new { p.Id, p.Name, p.Price });

        return MeasureAsync(mode, ct => query.ToListAsync(ct), explain: query,
            notes: "Look at the generated LIKE. Index on Name can seek for a prefix only. For 'contains' you need full-text search (or a different design).");
    }

    /// <summary>3.6 Aggregation: is the FK index enough?</summary>
    [HttpGet("top-customers")]
    public Task<IActionResult> TopCustomers(int take = 10)
    {
        var query = Db.Orders
            .TagWith("Lab 3.6 top customers by spend")
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Orders = g.Count(), Spend = g.Sum(o => o.Total) })
            .OrderByDescending(x => x.Spend)
            .Take(take);

        return MeasureAsync("GROUP BY CustomerId, SUM(Total)", ct => query.ToListAsync(ct), explain: query,
            notes: "IX_Orders_CustomerId exists but doesn't contain Total. Does the optimizer use it? What changes with INCLUDE(Total)?");
    }
}
