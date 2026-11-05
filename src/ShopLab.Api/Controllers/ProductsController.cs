using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Data;
using ShopLab.Api.Dtos;

namespace ShopLab.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ShopDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductListItemDto>>> List(
        int? categoryId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Products.AsNoTracking();
        if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductListItemDto(p.Id, p.Name, p.Sku, p.Price, p.Category.Name,
                p.Reviews.Average(r => (double?)r.Rating)))
            .ToListAsync(ct);

        return new PagedResult<ProductListItemDto>(items, page, pageSize, total);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> Get(int id, CancellationToken ct)
    {
        var product = await db.Products
            .Where(p => p.Id == id)
            .Select(p => new ProductDetailDto(
                p.Id, p.Name, p.Sku, p.Price, p.IsDiscontinued, p.Category.Name,
                p.ProductTags.Select(t => t.Tag.Name).ToList(),
                p.Reviews.Count,
                p.Reviews.Average(r => (double?)r.Rating),
                p.Reviews.OrderByDescending(r => r.CreatedAt).Take(5)
                    .Select(r => new ReviewDto(r.Id, r.Rating, r.Comment, r.CreatedAt,
                        r.Customer.FirstName + " " + r.Customer.LastName))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return product is null ? NotFound() : product;
    }
}
