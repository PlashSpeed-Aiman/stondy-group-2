# ShopLab — EF Core optimization study project

An ASP.NET Core (.NET 10) controller-based Web API on EF Core 10 + SQL Server,
built for a study group to practice:

- **finding the SQL** EF Core generates (logging, `ToQueryString`, `TagWith`, interceptors)
- **fixing query shapes**: N+1, cartesian explosion / split queries, tracking, projections, pagination
- **adding indexes** through migrations and proving they helped (plans, logical reads)

The database is seeded with ~1M rows of deliberately skewed e-commerce data
(customers, products, orders, order lines, reviews, tags), so bad queries are actually slow.

## Quick start

Prerequisites: .NET 10 SDK, Docker (or any SQL Server 2019+ instance).

```bash
# 1. Start SQL Server
docker compose up -d          # wait until `docker ps` shows (healthy)

# 2. Restore the local dotnet-ef tool and create the first migration
dotnet tool restore
cd src/ShopLab.Api
dotnet ef migrations add InitialCreate

# 3. Run — applies migrations and seeds data on first start (~30-60 s)
dotnet run
```

Open **http://localhost:5080/scalar** for the interactive API UI, or use
`src/ShopLab.Api/ShopLab.Api.http` in Visual Studio / Rider / VS Code (REST Client).

> **Not using Docker?** Change `ConnectionStrings:Shop` in `appsettings.json`, e.g. LocalDB on Windows:
> `Server=(localdb)\\MSSQLLocalDB;Database=ShopLab;Trusted_Connection=True;TrustServerCertificate=True`
>
> **Smaller laptop?** Lower the numbers in the `Seed` section. To reseed: `dotnet ef database drop -f`, then run again.

## What's in the box

```
docker-compose.yml          SQL Server 2022 Developer
.config/dotnet-tools.json   pins dotnet-ef
docs/LABS.md                the exercises — start here
sql/diagnostics.sql         STATISTICS IO, plan cache, Query Store, index sizes
src/ShopLab.Api/
  Domain/Entities.cs        Customer, Product, Category, Tag, Order, OrderItem, Review
  Data/ShopDbContext.cs     model config + commented-out LAB 3 indexes
  Data/DataSeeder.cs        deterministic SqlBulkCopy seeder (Bogus)
  Diagnostics/              SQL capture interceptor + middleware
  Controllers/Labs/         Lab 1-6 — "naive vs better" endpoints
  Controllers/              Products / Customers / Orders / Diagnostics
```

## Seeing the SQL

| Technique | Where |
|---|---|
| Console log of every command (with parameter values) | `appsettings.Development.json` → `Microsoft.EntityFrameworkCore.Database.Command: Information` + `EnableSensitiveDataLogging()` in `Program.cs` |
| SQL without running it | add `?explain=true` to any lab endpoint (uses `ToQueryString()`) |
| Find your query in logs / plan cache | `.TagWith("...")` → emitted as a `-- comment` above the SQL |
| Per-request command count and time | response headers `X-Sql-Count`, `X-Sql-Duration-Ms` |
| History of recent requests + their SQL | `GET /api/diagnostics/requests` |
| Slow query warnings | `SqlCaptureInterceptor`, threshold in `Diagnostics:SlowQueryThresholdMs` |
| Index usage / missing-index hints | `GET /api/diagnostics/indexes`, `GET /api/diagnostics/missing-indexes` |
| Plans and logical reads | paste the SQL into `sql/diagnostics.sql` in SSMS / VS Code mssql |

Every lab endpoint returns the same shape, so variants are easy to compare:

```json
{
  "lab": "Lab 1 - N+1 queries",
  "variant": "naive: query per row",
  "elapsedMs": 182.4,
  "sqlCommands": 101,
  "sqlDurationMs": 95.3,
  "rows": 50,
  "trackedEntities": 1240,
  "notes": "...",
  "firstSqlCommands": ["-- Lab 1 naive: latest orders\nSELECT TOP(@p) ..."],
  "result": [ ...first 5 rows... ]
}
```

## Useful EF CLI commands

```bash
dotnet ef migrations add Add_IX_Customers_Email
dotnet ef database update
dotnet ef migrations remove                 # undo the last (unapplied) migration
dotnet ef database update <PreviousName>    # roll back
dotnet ef migrations script --idempotent    # see the DDL an index migration produces
```
