using Microsoft.EntityFrameworkCore;
using ShopLab.Api.Domain;

namespace ShopLab.Api.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lengths matter: nvarchar(max) columns cannot be index keys in SQL Server.
        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Country).HasMaxLength(100);
        });

        modelBuilder.Entity<Category>(e => e.Property(x => x.Name).HasMaxLength(100));

        modelBuilder.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Sku).HasMaxLength(32);
            e.Property(x => x.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Tag>(e => e.Property(x => x.Name).HasMaxLength(50));

        modelBuilder.Entity<ProductTag>(e =>
        {
            e.HasKey(x => new { x.ProductId, x.TagId });
            e.HasOne(x => x.Product).WithMany(p => p.ProductTags).HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.Tag).WithMany(t => t.ProductTags).HasForeignKey(x => x.TagId);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.HasOne(x => x.Customer).WithMany(c => c.Orders).HasForeignKey(x => x.CustomerId);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId);
            e.HasOne(x => x.Product).WithMany(p => p.OrderItems).HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasOne(x => x.Product).WithMany(p => p.Reviews).HasForeignKey(x => x.ProductId);
            e.HasOne(x => x.Customer).WithMany(c => c.Reviews).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureLabIndexes(modelBuilder);
    }

    /// <summary>
    /// LAB 3 — INDEXES.
    /// Uncomment ONE index at a time, then run:
    ///   dotnet ef migrations add Add_&lt;IndexName&gt;
    ///   dotnet ef database update
    /// Re-run the matching /api/labs/3-indexes endpoint and compare the plan,
    /// logical reads (sql/diagnostics.sql) and the elapsed time.
    ///
    /// Reminder: EF Core already created an index for every foreign key
    /// (IX_Orders_CustomerId, IX_OrderItems_OrderId, ...). Look at them with
    /// GET /api/diagnostics/indexes before adding more.
    /// </summary>
    private static void ConfigureLabIndexes(ModelBuilder modelBuilder)
    {
        // 3.1 Point lookup — GET /api/labs/3-indexes/customer-by-email
        // modelBuilder.Entity<Customer>().HasIndex(c => c.Email).IsUnique();

        // 3.2 Composite index, column order matters — GET /api/labs/3-indexes/customers-by-location
        // modelBuilder.Entity<Customer>().HasIndex(c => new { c.Country, c.City });

        // 3.3 Equality + range + covering columns — GET /api/labs/3-indexes/orders-by-status
        // modelBuilder.Entity<Order>()
        //     .HasIndex(o => new { o.Status, o.OrderDate })
        //     .IncludeProperties(o => new { o.CustomerId, o.Total });

        // 3.4 Sargability — GET /api/labs/3-indexes/orders-in-year?mode=nonsargable vs mode=sargable
        //     Also used by Lab 5 (keyset pagination on OrderDate, Id).
        // modelBuilder.Entity<Order>().HasIndex(o => new { o.OrderDate, o.Id });

        // 3.5 Prefix search: helps StartsWith, does NOT help Contains — GET /api/labs/3-indexes/product-search
        // modelBuilder.Entity<Product>().HasIndex(p => p.Name);

        // 3.6 Covering an aggregate — GET /api/labs/3-indexes/top-customers
        //     The FK convention already created IX_Orders_CustomerId — why is it not enough here?
        //     Calling HasIndex on the same column reconfigures that existing index (no duplicate).
        // modelBuilder.Entity<Order>().HasIndex(o => o.CustomerId).IncludeProperties(o => o.Total);

        // 3.7 Filtered index — only the rows you query often
        // modelBuilder.Entity<Order>().HasIndex(o => o.OrderDate)
        //     .HasFilter("[Status] = 0") // Pending
        //     .HasDatabaseName("IX_Orders_Pending_OrderDate");
    }
}
