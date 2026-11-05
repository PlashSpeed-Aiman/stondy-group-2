using System.Data;
using System.Diagnostics;
using Bogus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShopLab.Api.Domain;

namespace ShopLab.Api.Data;

public sealed class SeedOptions
{
    public bool Enabled { get; set; } = true;
    public int RandomSeed { get; set; } = 42;
    public int Customers { get; set; } = 50_000;
    public int Products { get; set; } = 2_000;
    public int Orders { get; set; } = 250_000;
    public int MaxItemsPerOrder { get; set; } = 6;
    public int Reviews { get; set; } = 100_000;
}

/// <summary>
/// Generates a deterministic, deliberately skewed data set (a few customers and
/// products are very "hot") so that index selectivity and plan choices are
/// interesting. Uses SqlBulkCopy instead of SaveChanges so ~1M rows load in seconds.
/// </summary>
public sealed class DataSeeder(ShopDbContext db, IOptions<SeedOptions> options, ILogger<DataSeeder> logger)
{
    private const int ChunkSize = 50_000;

    private static readonly string[] CategoryNames =
    [
        "Books", "Electronics", "Computers", "Home", "Garden", "Kitchen", "Toys", "Games", "Sports", "Outdoors",
        "Beauty", "Health", "Grocery", "Automotive", "Tools", "Music", "Movies", "Clothing", "Shoes", "Jewelry"
    ];

    private static readonly string[] TagNames =
    [
        "new", "sale", "bestseller", "eco", "premium", "budget", "gift", "limited", "imported", "local",
        "handmade", "refurbished", "bundle", "clearance", "trending", "seasonal", "organic", "wireless",
        "portable", "kids", "pro", "classic", "compact", "waterproof", "vegan", "smart", "vintage",
        "durable", "lightweight", "exclusive"
    ];

    // Few countries, few cities each -> realistic low/medium-selectivity columns.
    private static readonly (string Country, string[] Cities, int Weight)[] Locations =
    [
        ("Malaysia", ["Kuala Lumpur", "Penang", "Johor Bahru", "Kota Kinabalu", "Ipoh"], 30),
        ("Singapore", ["Singapore"], 10),
        ("Indonesia", ["Jakarta", "Surabaya", "Bandung", "Medan"], 15),
        ("Thailand", ["Bangkok", "Chiang Mai", "Phuket"], 10),
        ("Philippines", ["Manila", "Cebu", "Davao"], 8),
        ("Vietnam", ["Hanoi", "Ho Chi Minh City", "Da Nang"], 8),
        ("Japan", ["Tokyo", "Osaka", "Fukuoka"], 6),
        ("Australia", ["Sydney", "Melbourne", "Perth"], 5),
        ("United Kingdom", ["London", "Manchester"], 4),
        ("United States", ["New York", "Seattle", "Austin"], 4)
    ];

    private static readonly (OrderStatus Status, int Weight)[] StatusWeights =
    [
        (OrderStatus.Delivered, 70), (OrderStatus.Shipped, 8), (OrderStatus.Paid, 5),
        (OrderStatus.Pending, 7), (OrderStatus.Cancelled, 7), (OrderStatus.Refunded, 3)
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.Enabled) return;

        if (await db.Customers.AnyAsync(ct))
        {
            logger.LogInformation("Database already contains data - skipping seed.");
            return;
        }

        logger.LogInformation(
            "Seeding: {Customers:N0} customers, {Products:N0} products, {Orders:N0} orders, {Reviews:N0} reviews. This takes a little while...",
            o.Customers, o.Products, o.Orders, o.Reviews);

        var total = Stopwatch.StartNew();
        var faker = new Faker("en") { Random = new Randomizer(o.RandomSeed) };
        var rng = new Random(o.RandomSeed);
        var now = DateTime.UtcNow;

        var conn = (SqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            // ---- Categories -------------------------------------------------
            var categories = NewTable(("Id", typeof(int)), ("Name", typeof(string)));
            for (var i = 0; i < CategoryNames.Length; i++)
                categories.Rows.Add(i + 1, CategoryNames[i]);
            await BulkInsertAsync<Category>(conn, categories, ct);

            // ---- Tags -------------------------------------------------------
            var tags = NewTable(("Id", typeof(int)), ("Name", typeof(string)));
            for (var i = 0; i < TagNames.Length; i++)
                tags.Rows.Add(i + 1, TagNames[i]);
            await BulkInsertAsync<Tag>(conn, tags, ct);

            // ---- Customers --------------------------------------------------
            var customerCreatedAt = new DateTime[o.Customers + 1];
            var customers = NewTable(
                ("Id", typeof(int)), ("FirstName", typeof(string)), ("LastName", typeof(string)),
                ("Email", typeof(string)), ("City", typeof(string)), ("Country", typeof(string)),
                ("IsVip", typeof(bool)), ("CreatedAt", typeof(DateTime)));
            var locationTotal = Locations.Sum(l => l.Weight);
            for (var id = 1; id <= o.Customers; id++)
            {
                var first = faker.Name.FirstName();
                var last = faker.Name.LastName();
                var loc = PickWeighted(rng, Locations, l => l.Weight, locationTotal);
                var created = now.AddDays(-rng.Next(30, 365 * 4)).AddMinutes(-rng.Next(0, 1440));
                customerCreatedAt[id] = created;
                customers.Rows.Add(id, first, last,
                    $"{Slug(first)}.{Slug(last)}.{id}@example.com",
                    loc.Cities[rng.Next(loc.Cities.Length)], loc.Country, false, created);
            }
            await BulkInsertAsync<Customer>(conn, customers, ct);
            customers.Dispose();

            // ---- Products + tags -------------------------------------------
            var productPrice = new decimal[o.Products + 1];
            var products = NewTable(
                ("Id", typeof(int)), ("Name", typeof(string)), ("Sku", typeof(string)), ("Price", typeof(decimal)),
                ("IsDiscontinued", typeof(bool)), ("CategoryId", typeof(int)));
            var productTags = NewTable(("ProductId", typeof(int)), ("TagId", typeof(int)));
            for (var id = 1; id <= o.Products; id++)
            {
                var price = Math.Round((decimal)(5 + rng.NextDouble() * 495), 2);
                productPrice[id] = price;
                products.Rows.Add(id, faker.Commerce.ProductName(), $"SKU-{id:D6}", price,
                    rng.NextDouble() < 0.1, rng.Next(1, CategoryNames.Length + 1));

                foreach (var tagId in Enumerable.Range(1, TagNames.Length).OrderBy(_ => rng.Next()).Take(rng.Next(1, 5)))
                    productTags.Rows.Add(id, tagId);
            }
            await BulkInsertAsync<Product>(conn, products, ct);
            await BulkInsertAsync<ProductTag>(conn, productTags, ct);

            // ---- Orders + items (chunked to keep memory flat) ---------------
            var statusTotal = StatusWeights.Sum(s => s.Weight);
            var orderItemId = 0;
            for (var start = 1; start <= o.Orders; start += ChunkSize)
            {
                var end = Math.Min(start + ChunkSize - 1, o.Orders);
                var orders = NewTable(
                    ("Id", typeof(int)), ("CustomerId", typeof(int)), ("OrderDate", typeof(DateTime)),
                    ("Status", typeof(int)), ("Total", typeof(decimal)));
                var items = NewTable(
                    ("Id", typeof(int)), ("OrderId", typeof(int)), ("ProductId", typeof(int)),
                    ("Quantity", typeof(int)), ("UnitPrice", typeof(decimal)));

                for (var orderId = start; orderId <= end; orderId++)
                {
                    var customerId = Skewed(rng, o.Customers, 2.5);
                    var created = customerCreatedAt[customerId];
                    var orderDate = created.AddSeconds(rng.NextDouble() * (now - created).TotalSeconds);
                    var status = PickWeighted(rng, StatusWeights, s => s.Weight, statusTotal).Status;
                    // Orders from the last 2 weeks are mostly still in progress.
                    if ((now - orderDate).TotalDays < 14 && status == OrderStatus.Delivered)
                        status = rng.Next(2) == 0 ? OrderStatus.Pending : OrderStatus.Shipped;

                    decimal orderTotal = 0;
                    var lines = rng.Next(1, o.MaxItemsPerOrder + 1);
                    for (var l = 0; l < lines; l++)
                    {
                        var productId = Skewed(rng, o.Products, 1.8);
                        var qty = rng.Next(1, 5);
                        var price = productPrice[productId];
                        orderTotal += qty * price;
                        items.Rows.Add(++orderItemId, orderId, productId, qty, price);
                    }

                    orders.Rows.Add(orderId, customerId, orderDate, (int)status, orderTotal);
                }

                await BulkInsertAsync<Order>(conn, orders, ct);
                await BulkInsertAsync<OrderItem>(conn, items, ct);
                logger.LogInformation("  orders {End:N0}/{Total:N0} ({Items:N0} order lines so far)", end, o.Orders, orderItemId);
            }

            // ---- Reviews ----------------------------------------------------
            var reviews = NewTable(
                ("Id", typeof(int)), ("ProductId", typeof(int)), ("CustomerId", typeof(int)),
                ("Rating", typeof(int)), ("Comment", typeof(string)), ("CreatedAt", typeof(DateTime)));
            int[] ratingBag = [1, 2, 3, 3, 4, 4, 4, 5, 5, 5, 5];
            for (var id = 1; id <= o.Reviews; id++)
            {
                reviews.Rows.Add(id, Skewed(rng, o.Products, 1.8), rng.Next(1, o.Customers + 1),
                    ratingBag[rng.Next(ratingBag.Length)], faker.Lorem.Sentence(rng.Next(4, 16)),
                    now.AddDays(-rng.Next(0, 365 * 3)));
            }
            await BulkInsertAsync<Review>(conn, reviews, ct);
        }
        finally
        {
            await conn.CloseAsync();
        }

        logger.LogInformation("Seed finished in {Elapsed:N1}s", total.Elapsed.TotalSeconds);
    }

    private async Task BulkInsertAsync<TEntity>(SqlConnection conn, DataTable data, CancellationToken ct)
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not in the model.");
        var table = $"[{entityType.GetSchema() ?? "dbo"}].[{entityType.GetTableName()}]";

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = table,
            BatchSize = 10_000,
            BulkCopyTimeout = 0
        };
        foreach (DataColumn column in data.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulk.WriteToServerAsync(data, ct);
    }

    private static DataTable NewTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns)
            table.Columns.Add(name, type);
        return table;
    }

    /// <summary>Returns 1..max, biased towards low ids (higher exponent = more skew).</summary>
    private static int Skewed(Random rng, int max, double exponent) =>
        Math.Clamp((int)(Math.Pow(rng.NextDouble(), exponent) * max) + 1, 1, max);

    private static T PickWeighted<T>(Random rng, IReadOnlyList<T> items, Func<T, int> weight, int totalWeight)
    {
        var roll = rng.Next(totalWeight);
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll < 0) return item;
        }
        return items[^1];
    }

    private static string Slug(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
