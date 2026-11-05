using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers.Labs;

/// <summary>
/// LAB 1 — The N+1 problem.
/// Same output, three ways: one query per row, eager loading, projection.
/// Watch <c>sqlCommands</c> in the response and the SQL in the console.
/// </summary>
[Route("api/labs/1-n-plus-one")]
public sealed class Lab1NPlusOneController(ShopDbContext db, SqlCapture sql) : LabControllerBase(db, sql)
{
    protected override string LabName => "Lab 1 - N+1 queries";

    /// <summary>1 query for the orders + 2 more for EACH order.</summary>
    [HttpGet("naive")]
    public Task<IActionResult> Naive(int take = 50)
    {
        var ordersQuery = Db.Orders
            .TagWith("Lab 1 naive: latest orders")
            .OrderByDescending(o => o.OrderDate)
            .Take(take);

        return MeasureAsync("naive: query per row", async ct =>
        {
            var orders = await ordersQuery.ToListAsync(ct);
            var result = new List<OrderSummaryDto>(orders.Count);

            foreach (var order in orders)
            {
                var customer = await Db.Customers
                    .TagWith("Lab 1 naive: customer for one order")
                    .SingleAsync(c => c.Id == order.CustomerId, ct);

                var items = await Db.OrderItems
                    .TagWith("Lab 1 naive: items for one order")
                    .Where(i => i.OrderId == order.Id)
                    .ToListAsync(ct);

                result.Add(new OrderSummaryDto(order.Id, order.OrderDate, order.Status.ToString(), customer.Email,
                    items.Count, items.Sum(i => i.Quantity * i.UnitPrice)));
            }

            return result;
        }, explain: ordersQuery,
        notes: $"Expect 1 + 2×{take} commands. This is exactly what lazy loading does silently.");
    }

    /// <summary>Include() the navigations: one round trip, but full entities (all columns) come back.</summary>
    [HttpGet("include")]
    public Task<IActionResult> Include(int take = 50)
    {
        var query = Db.Orders
            .TagWith("Lab 1 include")
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .Take(take);

        return MeasureAsync("eager loading with Include", async ct =>
        {
            var orders = await query.ToListAsync(ct);
            return orders.Select(o => new OrderSummaryDto(o.Id, o.OrderDate, o.Status.ToString(), o.Customer.Email,
                o.Items.Count, o.Items.Sum(i => i.Quantity * i.UnitPrice))).ToList();
        }, explain: query,
        notes: "1 command. Look at the SELECT list: every Customer and OrderItem column is fetched even though we use 3 of them.");
    }

    /// <summary>Project to exactly what is needed; aggregation happens in SQL.</summary>
    [HttpGet("projection")]
    public Task<IActionResult> Projection(int take = 50)
    {
        var query = Db.Orders
            .TagWith("Lab 1 projection")
            .OrderByDescending(o => o.OrderDate)
            .Take(take)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderDate,
                o.Status.ToString(),
                o.Customer.Email,
                o.Items.Count,
                o.Items.Sum(i => i.Quantity * i.UnitPrice)));

        return MeasureAsync("projection with Select", ct => query.ToListAsync(ct), explain: query,
            notes: "1 command, only the needed columns, COUNT/SUM computed by SQL Server. No tracking needed (projections aren't tracked).");
    }
}
