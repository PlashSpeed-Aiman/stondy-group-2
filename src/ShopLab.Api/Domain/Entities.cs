namespace ShopLab.Api.Domain;

// The model is intentionally "plain": no indexes beyond what EF Core creates by
// convention (primary keys + one index per foreign key). Adding the right ones
// is the point of Lab 3 — see ShopDbContext.ConfigureLabIndexes.

public class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public bool IsVip { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Order> Orders { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsDiscontinued { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public List<OrderItem> OrderItems { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    public List<ProductTag> ProductTags { get; set; } = [];
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public List<ProductTag> ProductTags { get; set; } = [];
}

/// <summary>Explicit many-to-many join entity (Product &lt;-&gt; Tag).</summary>
public class ProductTag
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4,
    Refunded = 5
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }

    /// <summary>Denormalised sum of the order lines (handy for index/covering experiments).</summary>
    public decimal Total { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
