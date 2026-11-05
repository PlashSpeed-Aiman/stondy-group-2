using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(ShopDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Get(int id, CancellationToken ct)
    {
        var customer = await db.Customers
            .Where(c => c.Id == id)
            .Select(c => new CustomerDto(c.Id, c.FirstName + " " + c.LastName, c.Email, c.City, c.Country, c.IsVip,
                c.Orders.Count))
            .FirstOrDefaultAsync(ct);

        return customer is null ? NotFound() : customer;
    }

    [HttpGet("{id:int}/orders")]
    public async Task<ActionResult<IReadOnlyList<OrderRowDto>>> Orders(int id, int take = 20, CancellationToken ct = default)
    {
        var orders = await db.Orders
            .Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.OrderDate)
            .Take(Math.Clamp(take, 1, 200))
            .Select(o => new OrderRowDto(o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId))
            .ToListAsync(ct);

        return orders;
    }
}
