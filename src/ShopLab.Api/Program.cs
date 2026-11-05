using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ShopLab.Api.Data;
using ShopLab.Api.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Lab endpoints return entity graphs (Order -> Items -> Order ...)
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection("Seed"));
builder.Services.Configure<DiagnosticsOptions>(builder.Configuration.GetSection("Diagnostics"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SqlCapture>();
builder.Services.AddSingleton<RecentRequestStore>();
builder.Services.AddSingleton<SqlCaptureInterceptor>();
builder.Services.AddScoped<DataSeeder>();

builder.Services.AddDbContext<ShopDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shop"), sql =>
    {
        // LAB 2: try switching the *default* for every query that has multiple Includes.
        // sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    });

    options.AddInterceptors(sp.GetRequiredService<SqlCaptureInterceptor>());

    if (builder.Environment.IsDevelopment())
    {
        // Shows parameter VALUES in logs (never in production: leaks data).
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

var app = builder.Build();

// ---- Database: migrate + seed --------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    if (!db.Database.GetMigrations().Any())
    {
        throw new InvalidOperationException(
            "No EF Core migrations found. From src/ShopLab.Api run:\n" +
            "  dotnet tool restore\n" +
            "  dotnet ef migrations add InitialCreate\n" +
            "then start the app again.");
    }

    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
}

// ---- HTTP pipeline ----------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();              // /openapi/v1.json
    app.MapScalarApiReference();   // /scalar  (interactive API UI)
}

app.UseMiddleware<SqlCaptureMiddleware>();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.Run();
