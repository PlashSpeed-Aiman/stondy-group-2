namespace ShopLab.Api.Dtos;

// ---- Regular API -------------------------------------------------------------

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProductListItemDto(int Id, string Name, string Sku, decimal Price, string Category, double? AverageRating);

public sealed record ProductDetailDto(
    int Id, string Name, string Sku, decimal Price, bool IsDiscontinued, string Category,
    IReadOnlyList<string> Tags, int ReviewCount, double? AverageRating, IReadOnlyList<ReviewDto> LatestReviews);

public sealed record ReviewDto(int Id, int Rating, string Comment, DateTime CreatedAt, string Customer);

public sealed record CustomerDto(int Id, string Name, string Email, string City, string Country, bool IsVip, int OrderCount);

public sealed record OrderDetailDto(
    int Id, DateTime OrderDate, string Status, decimal Total, int CustomerId, string CustomerEmail,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record OrderLineDto(int ProductId, string ProductName, int Quantity, decimal UnitPrice);

// ---- Labs ----------------------------------------------------------------------

public sealed record OrderSummaryDto(int Id, DateTime OrderDate, string Status, string CustomerEmail, int ItemCount, decimal ItemsTotal);

public sealed record ProductGraphDto(int Id, string Name, int Reviews, int OrderItems, IReadOnlyList<string> Tags, long CartesianRowsInSingleQuery);

public sealed record OrderRowDto(int Id, DateTime OrderDate, string Status, decimal Total, int CustomerId);

public sealed record KeysetPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>What every lab endpoint returns, so variants are easy to compare.</summary>
public sealed record LabResult(
    string Lab,
    string Variant,
    double ElapsedMs,
    int SqlCommands,
    double SqlDurationMs,
    int? Rows,
    int TrackedEntities,
    string? Notes,
    IReadOnlyList<string> FirstSqlCommands,
    object? Result);
