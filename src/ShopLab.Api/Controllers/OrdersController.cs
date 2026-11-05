using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(ShopDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailDto>> Get(int id, CancellationToken ct)
    {
        var order = await db.Orders
            .Where(o => o.Id == id)
            .Select(o => new OrderDetailDto(
                o.Id, o.OrderDate, o.Status.ToString(), o.Total, o.CustomerId, o.Customer.Email,
                o.Items.Select(i => new OrderLineDto(i.ProductId, i.Product.Name, i.Quantity, i.UnitPrice)).ToList()))
            .FirstOrDefaultAsync(ct);

        return order is null ? NotFound() : order;
    }
}
