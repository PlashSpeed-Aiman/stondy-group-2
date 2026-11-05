# Labs

Suggested session format (60–90 min per lab):
1. **Predict**: before running, everyone writes down what SQL they expect and how many commands.
2. **Run** the "naive" variant. Read the SQL in the console / `firstSqlCommands` / `?explain=true`.
3. **Measure** it properly in SSMS: actual execution plan + `SET STATISTICS IO ON` (`sql/diagnostics.sql`).
4. **Fix** and run the better variant. Compare `sqlCommands`, logical reads, `elapsedMs`.
5. **Discuss** the trade-off — every fix in here has one.

Timings on a laptop are noisy. Run each variant 3–5 times and ignore the first run
(plan compilation + cold buffer pool). **Logical reads** are the stable number to compare.
Use `POST /api/diagnostics/cold-cache` when you want a deliberately cold run.

---

## Lab 1 — N+1 queries

Endpoints: `/api/labs/1-n-plus-one/{naive|include|projection}?take=50`

- `naive` loads 50 orders, then queries the customer and the items **for each order** → 101 commands.
- `include` uses `Include()` → 1 command, but look at the `SELECT` list.
- `projection` uses `Select()` into a DTO → 1 command, only needed columns, `COUNT`/`SUM` in SQL.

Exercises
- Change `take` to 500. How does each variant scale?
- Copy the `include` SQL into SSMS. How many rows come back vs. how many orders? Why?
- Turn on lazy loading (`Microsoft.EntityFrameworkCore.Proxies`, `UseLazyLoadingProxies()`, make navigations `virtual`) and rewrite `include` without `Include`. Count the commands. Then turn it off again. 🙂

Discuss: when is `Include` better than a projection? (Hint: when you're about to modify and save the entities.)

---

## Lab 2 — Cartesian explosion & split queries

Endpoints: `/api/labs/2-split-query/{single|split|filtered-include}?take=3&hot=false`

A product with 3 collections loaded in one query returns `reviews × orderItems × tags` rows.
Each result row reports `cartesianRowsInSingleQuery`.

Exercises
- Run `single` and `split` with `hot=false`, then `take=1&hot=true`. Compare elapsed time and data transferred.
- Find the `MultipleCollectionIncludeWarning` in the console log.
- Uncomment `UseQuerySplittingBehavior(SplitQuery)` in `Program.cs`. Which endpoints change? Why does `single` still produce one query?
- `split` sends N queries. Look at them: what is repeated in each? What happens if a row is inserted between query 1 and 2?
- `filtered-include`: often the real fix is to load less.

---

## Lab 3 — Indexes

Endpoints: `/api/labs/3-indexes/...` — start with `sample-values`.
Indexes to add: `ShopDbContext.ConfigureLabIndexes` (commented out, one per exercise).

Before anything: `GET /api/diagnostics/indexes`. EF Core already created an index for every foreign key —
know what you have before adding more.

Workflow for every exercise:
```bash
# baseline: run the endpoint a few times, capture plan + logical reads in SSMS
# uncomment ONE index in ConfigureLabIndexes, then:
dotnet ef migrations add Add_<Name>
dotnet ef migrations script <Previous> Add_<Name>   # read the DDL
dotnet ef database update
# run again and compare
```

| # | Endpoint | Question |
|---|---|---|
| 3.1 | `customer-by-email` | Scan → Seek. Why is there now a Key Lookup? Would `IsUnique()` change the plan? |
| 3.2 | `customers-by-location` | Does `(Country, City)` help a query on `City` only? Try `(City, Country)`. |
| 3.3 | `orders-by-status` | Equality column first, range second. Remove the `INCLUDE` — what appears in the plan? |
| 3.4 | `orders-in-year?mode=…` | With the index in place, only `sargable` seeks. Why? |
| 3.5 | `product-search?mode=…` | `startswith` vs `contains` vs `endswith` with an index on `Name`. |
| 3.6 | `top-customers` | Is `IX_Orders_CustomerId` used? What changes with `INCLUDE(Total)`? |
| 3.7 | filtered index | Compare size (`sql/diagnostics.sql` §3) of the filtered vs a full index. |

Also check `GET /api/diagnostics/missing-indexes` after running the baselines — and argue about
whether you'd actually create what it suggests (hint: it never considers write cost or overlap).

Write-cost exercise: run `POST /api/labs/6-pitfalls/vip-execute-update` before and after adding
several indexes on `Customers`. Indexes aren't free.

---

## Lab 4 — Tracking

Endpoints: `/api/labs/4-tracking/{tracked|no-tracking|no-tracking-identity-resolution|projection}?take=20000`

The SQL is the same for the first three — compare `elapsedMs - sqlDurationMs` (the .NET-side cost)
and `trackedEntities`.

Exercises
- Why does `no-tracking` create more `Customer` objects than `tracked`?
- Set the default for the whole context: `options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)`. What breaks?
- Profile memory with `dotnet-counters monitor -n ShopLab.Api` while hitting `tracked` repeatedly.

---

## Lab 5 — Pagination

Endpoints: `/api/labs/5-pagination/offset?page=…` and `/keyset?cursor=…`

Exercises
- `offset` page 1 vs page 10000. Compare logical reads in SSMS.
- `includeTotal=true`: how much of the cost is the `COUNT(*)`?
- Add index 3.4 `(OrderDate, Id)`. Which variant benefits more?
- Look at the keyset `WHERE`. Why do we need `Id` as a tie-breaker?

---

## Lab 6 — Pitfalls

Endpoints: `/api/labs/6-pitfalls/...`

- `count-client` vs `count-server` — where does the `WHERE` run?
- `exists-count` vs `exists-any` (customer 1 has the most orders).
- `vip-tracked` vs `vip-execute-update` — count the commands; then try `MaxBatchSize(1)` in `UseSqlServer(...)`.

Extra ideas for later sessions
- Compiled queries (`EF.CompileAsyncQuery`) on a hot endpoint — measure with BenchmarkDotNet.
- `dotnet ef dbcontext optimize` (compiled models) — startup time with a big model.
- Write an interceptor that throws in Development when a single request issues more than 20 commands.
- Parameter sniffing: run `orders-by-status` with `Pending` then `Delivered` and inspect the cached plan.
- Raw SQL with `FromSql` vs LINQ: compare plans for a window-function query.
