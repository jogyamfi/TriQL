# TriQL.EntityFrameworkCore

An Entity Framework Core 10 provider for [Trino](https://trino.io), built on the TriQL ADO.NET
provider. It supports LINQ queries on any connector, raw SQL, `SaveChanges`,
`ExecuteUpdate`/`ExecuteDelete`, `EnsureCreated`/`EnsureDeleted` for development, and reverse
engineering (`dotnet ef dbcontext scaffold`). Writes are designed for and verified against the
Iceberg connector.

> **Status: preview** (`1.1.0-preview.*`). Every feature below is covered by unit and SQL-baseline
> tests, and by a live functional suite that runs in CI against Trino 466 (the oldest supported
> version) and the latest release, with the `memory`, `tpch` and Iceberg connectors.

## Contents

- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Modelling for Trino and Iceberg](#modelling-for-trino-and-iceberg)
- [Type mapping](#type-mapping)
- [Querying](#querying)
- [Saving data](#saving-data)
- [Bulk operations](#bulk-operations)
- [Retries](#retries)
- [Creating tables for development](#creating-tables-for-development)
- [Scaffolding an existing database](#scaffolding-an-existing-database)
- [Differences from SQL Server](#differences-from-sql-server)
- [Performance tips](#performance-tips)
- [Diagnostics](#diagnostics)
- [Testing against your cluster](#testing-against-your-cluster)

## Getting started

```shell
dotnet add package TriQL.EntityFrameworkCore --prerelease
```

```csharp
public class Order
{
    public Guid Id { get; set; }              // generated in .NET (time-ordered UUIDv7)
    public string Customer { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly OrderDate { get; set; }
    public long Version { get; set; }         // a concurrency token you maintain
}

public class SalesContext(DbContextOptions<SalesContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sales");
        modelBuilder.Entity<Order>(e =>
        {
            e.Property(o => o.Amount).HasPrecision(12, 2);
            e.Property(o => o.Version).IsConcurrencyToken();
        });
    }
}

builder.Services.AddDbContext<SalesContext>(options => options.UseTrino(
    "Server=https://trino.example.com;Catalog=lake;Schema=sales;User=analyst;Password=…",
    o => o.EnableRetryOnFailure()));
```

```csharp
var top = await db.Orders
    .Where(o => o.OrderDate >= new DateOnly(2026, 1, 1))
    .GroupBy(o => o.Customer)
    .Select(g => new { Customer = g.Key, Total = g.Sum(o => o.Amount) })
    .OrderByDescending(x => x.Total)
    .Take(10)
    .ToListAsync();

db.Orders.Add(new Order { Customer = "ACME", Amount = 99.50m, OrderDate = DateOnly.FromDateTime(DateTime.UtcNow) });
await db.SaveChangesAsync();
```

## Configuration

### Connecting

`UseTrino` takes, in order of preference:

| Overload | Use when |
|---|---|
| `UseTrino(string connectionString, …)` | The usual case. Every [connection-string keyword](connection-string-reference.md) applies. |
| `UseTrino(TrinoDataSource dataSource, …)` | Many contexts should share one HTTP handler (and its TLS connections), for example without context pooling. |
| `UseTrino(TrinoSessionOptions options, …)` | You build session options in code (for example with an authenticator). |
| `UseTrino(DbConnection connection, [bool contextOwnsConnection], …)` | You manage the `TrinoConnection` yourself. |

Calling `UseTrino` again replaces the connection configured earlier.

Connections created from a connection string use the **UTC session time zone** unless the
connection string sets `TimeZone`, so `DateTime.Now`, timestamp casts and the parts of
`DateTimeOffset` values give the same results on every machine. Turn this off with
`o.UseUtcSessionTimeZone(false)` to use the client machine's zone. Connections supplied as a
`DbConnection`, `TrinoSessionOptions` or `TrinoDataSource` keep their own setting.

### Provider options

Set through the optional action: `UseTrino(…, o => o.EnableRetryOnFailure().MaxBatchSize(500))`.

| Option | Default | Effect |
|---|---|---|
| `EnableRetryOnFailure(maxRetryCount = 6, maxRetryDelay = 30 s)` | off | Retries transient failures. See [Retries](#retries). |
| `MaxBatchSize(n)` | 1000 | The most rows in one combined `INSERT`. `MaxBatchSize(1)` sends one statement per row. |
| `UseUtcSessionTimeZone(bool)` | `true` | See above. |
| `CommandTimeout(seconds)` | none | A client-side timeout for each statement. |

The connection-string keyword `ParameterBinding=ExecuteImmediate` sends parameterized SQL in the
request body (`EXECUTE IMMEDIATE`) instead of an HTTP header. Use it behind a proxy or gateway that
limits header sizes (8–16 KB is common): large `IN` lists and multi-row inserts produce long SQL.

## Modelling for Trino and Iceberg

### Keys and generated values

Trino and Iceberg generate no values: there are no identity columns, sequences, column defaults,
computed columns or row versions, and no `RETURNING`. The provider follows from that:

- An **integer key** is not treated as database-generated: set it in .NET.
- A **`Guid` key** is generated in .NET as a version 7 (time-ordered) UUID and stored as `uuid`.
- `string` and `byte[]` keys, and any property with `HasValueGenerator`, are generated in .NET too.

The model validator rejects, when the model is built and with a message saying what to do instead:
`ValueGeneratedOnAdd` without a .NET generator, `ValueGeneratedOnUpdate`, `HasDefaultValue`,
`HasDefaultValueSql`, `HasComputedColumnSql`, `IsRowVersion`, sequences (and so HiLo), and an
`OwnsMany` collection that keeps EF's default key (give the owned type a `Guid` key with
`HasKey("Id")`).

Trino enforces no keys or uniqueness. Unique indexes and alternate keys are allowed but logged as
`TrinoEventId.UniqueIndexNotEnforced` warnings, because duplicates can still be written.

### Catalogs and schemas

Tables resolve against the connection's catalog and schema unless the model says otherwise:

```csharp
modelBuilder.HasDefaultCatalog("lake").HasDefaultSchema("sales");                   // every table
modelBuilder.Entity<Customer>().HasCatalog("crm").ToTable("customers", "public");    // one entity
```

The SQL then uses three-part names (`"lake"."sales"."orders"`) in queries, joins across catalogs,
`SaveChanges`, `ExecuteUpdate`/`ExecuteDelete` and DDL. A table in a catalog must also have a schema.
Owned and derived types follow their owner's and base type's catalog.

### Naming

Trino stores identifiers in lower case and matches them case-insensitively, so EF's PascalCase
names work as they are (`"OrderDate"` finds `orderdate`). Names that differ only by case are
rejected by the model validator, because they would name the same table or column. For snake_case
tables, either map names explicitly (`HasColumnName`), scaffold the model (which does it for you),
or use [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions)
(`UseSnakeCaseNamingConvention()`).

### Views and keyless types

Map views and query types with `ToView(...)`, `ToSqlQuery(...)` or `HasNoKey()`, as with any
relational provider.

## Type mapping

| .NET | Trino (default store type) | Notes |
|---|---|---|
| `bool` | `boolean` | |
| `sbyte` | `tinyint` | Iceberg stores `integer`; values read back unchanged. |
| `byte` | `smallint` | Trino has no unsigned types. |
| `short` | `smallint` | Iceberg stores `integer`. |
| `ushort` | `integer` | |
| `int` | `integer` | |
| `uint` | `bigint` | |
| `long` | `bigint` | |
| `ulong` | `decimal(20,0)` | |
| `float` | `real` | |
| `double` | `double` | |
| `decimal` | `decimal(18,2)` | `HasPrecision(p, s)`, `p` ≤ 38. |
| `string` | `varchar` | `HasMaxLength(n)` → `varchar(n)`; `IsFixedLength()` → `char(n)`. Iceberg stores `varchar`. |
| `char` | `varchar(1)` | |
| `byte[]` | `varbinary` | |
| `Guid` | `uuid` | |
| `DateOnly` | `date` | |
| `TimeOnly` | `time(6)` | |
| `DateTime` | `timestamp(6)` | `HasPrecision(p)`, up to 12; .NET keeps 100 ns. Iceberg stores `timestamp(6)`. |
| `DateTimeOffset` | `timestamp(6) with time zone` | Iceberg stores the instant in UTC: values read back with offset zero. |
| `TimeSpan` | `bigint` | Ticks; `interval day to second` is not a column type on most connectors. |
| enums | the underlying integer type | EF's default conversion. |

Columns of other Trino types (`array`, `map`, `row`, `json`, `ipaddress`, intervals) are not
mapped. Read them with raw SQL that converts them, for example to `varchar` with `json_format`.
See the [type-mapping reference](type-mapping-reference.md) for how the ADO.NET provider reads each
Trino type.

## Querying

Standard LINQ translates: filtering, projection, ordering, paging, `GroupBy` with aggregates and
`HAVING`, joins, `Include` (single and split queries), set operations, owned types, keyless types,
`FromSql`/`SqlQuery`, compiled queries, and `Contains` on lists (expanded to `IN (@p1, @p2, …)`).

Paging generates `OFFSET m LIMIT n`, with both values as parameters. String `+` generates `||`, and
`CROSS APPLY`/`OUTER APPLY` generate `CROSS JOIN LATERAL`/`LEFT JOIN LATERAL … ON TRUE`.

### Translated members and methods

| Area | Translated |
|---|---|
| `string` | `Length`, `ToUpper`/`ToLower`, `Trim`/`TrimStart`/`TrimEnd` (no argument or one `char`), `Substring`, `IndexOf`, `Replace`, `Contains`/`StartsWith`/`EndsWith`, `string.IsNullOrEmpty`/`IsNullOrWhiteSpace`, concatenation, `string.Compare` |
| `Regex` | `Regex.IsMatch(s, pattern[, RegexOptions.IgnoreCase])` → `regexp_like` |
| `Math` | `Abs`, `Ceiling`, `Floor`, `Truncate`, `Round`, `Sign`, `Max`, `Min`, `Pow`, `Sqrt`, `Cbrt`, `Exp`, `Log`, `Log10`, `Log2`, trigonometric and hyperbolic functions |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` | `Year` … `Millisecond`, `DayOfYear`, `DayOfWeek`, `Date`, `DateTime`, `UtcDateTime`, `Now`, `UtcNow`, `Today`, `AddYears` … `AddMilliseconds`, `DateOnly.FromDateTime` |
| Conversions | `Convert.ToBoolean/ToByte/ToDecimal/ToDouble/ToInt16/ToInt32/ToInt64/ToString` → `CAST` (never `TRY_CAST`); `ToString()` of integers, `decimal`, `Guid`, `bool`, `char` |
| Other | `Guid.NewGuid()` → `uuid()`, `byte[].Length` |
| Aggregates | `string.Join`/`string.Concat` over a group (ordered, filtered and distinct) → `array_join(array_agg(…))` |
| `EF.Functions` | `Like`, `ILike` (`lower(a) LIKE lower(b)`), `DateDiffYear` … `DateDiffSecond` (`date_diff`, whole units), `JsonExtractScalar`, `ApproxDistinct` (aggregate) |

### Semantics to be aware of

These are measured Trino behaviours. Where .NET differs, the provider either matches .NET or
documents the difference:

- **Null ordering matches .NET.** Ascending orderings of nullable values get `NULLS FIRST`; Trino
  would otherwise put `NULL` last.
- **`Math.Round` matches .NET.** Trino rounds half away from zero, so half-to-even is emulated for
  `double`/`decimal` rounded to an integer and `decimal` rounded to constant digits.
  `MidpointRounding.AwayFromZero` maps to Trino's `round`. A `double` rounded half-to-even to
  digits is not translated, because no SQL expression can match .NET there.
- **String positions are 1-based in Trino**; `Substring`/`IndexOf` convert. Trino counts Unicode
  code points where .NET counts UTF-16 units, so lengths and positions differ for characters
  outside the Basic Multilingual Plane (such as emoji).
- **`Trim()`** keeps non-breaking spaces (U+00A0, U+2007, U+202F) that .NET removes.
- **String comparison** is binary and case-sensitive, like C# `==` and `string.CompareOrdinal`.
- **`Regex.IsMatch`** uses Trino's regular-expression library (Joni, Ruby syntax), not .NET's.
- **`CAST(double AS integer)`** rounds half away from zero, while `Convert.ToInt32` rounds half to even.
- **`DateTime.Now`** is the session time zone's time, which is UTC by default.
- **`DayOfWeek`** is converted from Trino's ISO numbering (Monday = 1) to .NET's (Sunday = 0).
- **`EF.Functions.DateDiff*`** count *whole* units elapsed; SQL Server's `DATEDIFF` counts boundaries.
- **Not translated:** `ToString()` of `double`, `float` and dates (Trino's formatting differs),
  `Trim(params char[])`, `TimeOfDay`, `MathF`.

### Correlated subqueries

Trino supports only some correlated subqueries. These shapes are rejected with an
`InvalidOperationException` at translation time, before any SQL is sent:

- a subquery with `Skip` that refers to the outer row;
- a subquery whose *selected values* use an outer column (for example
  `b.Posts.Select(p => p.Title + b.Name)`);
- a subquery with `Take`/`First` inside `Contains` (rewrite it with `Any(x => x.Key == outer.Key)`);
- a subquery with `Take`/`First` or `GroupBy` that compares the outer row other than for equality
  (for example `p.Views > b.Id`).

Equality correlation (navigations, joins on keys) works with every kind of subquery, and EF already
rewrites "top N per parent" collection projections into a window-function join.

Split queries (`AsSplitQuery`) are buffered automatically, because a Trino connection runs one
command at a time.

## Saving data

### SaveChanges

`SaveChanges` sends one `INSERT`/`UPDATE`/`DELETE` per change, except that consecutive inserts into
one table are [combined](#bulk-operations).

- **No transaction.** Trino has no multi-statement transactions, so each statement commits on its
  own, and a `SaveChanges` with several statements is **not atomic**. If statement *k* fails, the
  entities of statements *1 … k-1* are marked as saved, so the next `SaveChanges` does not apply them
  twice; the failed and remaining ones keep their pending state. `TrinoEventId.NonAtomicSaveChanges`
  is logged once for each such save. `BeginTransaction`, `UseTransaction`, `EnlistTransaction` and
  `TransactionScope` throw `NotSupportedException`.
- **Concurrency.** An `UPDATE` or `DELETE` that changes no row throws `DbUpdateConcurrencyException`.
  Use a concurrency token that you maintain: a `long Version` marked `IsConcurrencyToken()` and
  incremented before each save. Resolve a conflict by reloading the entry and applying the change
  again.
- **Writes need a connector that supports them.** They are verified on Iceberg. Other connectors
  fail with the server's own error (for example "This connector does not support updates").
- Trino has no `RETURNING`, so nothing is read back after a save. The model validator rules out
  values that would have to be.

### Bulk operations

- **Multi-row inserts.** Consecutive inserts into the same table with the same columns become one
  `INSERT … VALUES (…), (…)` of up to `MaxBatchSize` rows (default 1000) and 2,000 parameters. This
  is one round trip and one Iceberg commit instead of one per row: in the benchmarks, 1,000 rows
  took 0.69 s combined against 283 s one statement per row.
- **`ExecuteDelete`** generates `DELETE FROM table WHERE …`. Filters on related tables, joins and
  `Take` become `EXISTS`/`IN` subqueries on the target.
- **`ExecuteUpdate`** generates `UPDATE table SET … WHERE …`. When the update involves another table
  (a filter on a related table, a value from one, or `Take`), Trino has no `UPDATE … FROM`, so the
  provider generates a `MERGE` matched on the primary key:

  ```sql
  MERGE INTO "Posts" AS "p"
  USING (SELECT "p"."Id" AS "k0", "b"."Name" AS "v0" FROM "Posts" AS "p" INNER JOIN "Blogs" AS "b" ON …) AS "triql_source"
  ON "p"."Id" = "triql_source"."k0"
  WHEN MATCHED THEN UPDATE SET "Title" = "triql_source"."v0"
  ```

  Each target row must match at most one source row, or Trino fails the `MERGE`. A table without a
  primary key cannot be updated this way.

Trino's `UPDATE` and `DELETE` take no table alias, so the provider names the target table without
one and qualifies its columns with the table name.

## Retries

`EnableRetryOnFailure()` retries failures that Trino reports as transient: the cluster starting up,
out of memory or losing a worker, a full query queue, and `ICEBERG_COMMIT_ERROR`, which concurrent
writers to one Iceberg table cause. Lost connections are retried too.

- **Queries, `ExecuteUpdate` and `ExecuteDelete`** are retried as a whole by the execution strategy.
- **`SaveChanges`** retries only the statement that failed, never the whole save, because earlier
  statements have already committed. It retries only when Trino reported that statement as failed,
  not after a lost connection, because the statement may have committed before the connection
  dropped. Concurrency conflicts are never retried.
- EF does not run raw SQL through the execution strategy. To retry it, wrap it:
  `await db.Database.CreateExecutionStrategy().ExecuteAsync(() => db.Database.ExecuteSqlAsync($"…"))`.

Iceberg commits optimistically. In the test suite, concurrent `SaveChanges` updating different rows
of one table mostly failed with `ICEBERG_COMMIT_ERROR` without retries, and all succeeded with
them, each applied exactly once.

## Creating tables for development

`EnsureCreated`, `EnsureDeleted` and `GenerateCreateScript` are for development and tests:

- `EnsureCreated` runs `CREATE SCHEMA IF NOT EXISTS` and `CREATE TABLE` (with `NOT NULL` and
  comments) when none of the model's tables exists. Keys, foreign keys and indexes are left out,
  because Trino has none. Table properties such as Iceberg's `format` or `partitioning` are left to
  the connector's defaults: create the table with SQL when you need them.
- `EnsureDeleted` drops **only the model's tables**, never schemas or catalogs, which may hold other
  data.
- Migrations are not supported: `Migrate()` and `dotnet ef migrations` throw. Manage production
  schemas with SQL or your deployment tooling.

## Scaffolding an existing database

```shell
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet ef dbcontext scaffold "Server=https://trino.example.com;Catalog=lake;Schema=sales;User=…" TriQL.EntityFrameworkCore -o Model
```

- One catalog per run: the connection's, or another named with `--schema catalog.schema` (which
  adds `HasDefaultCatalog`). `--table` accepts `table`, `schema.table` or `catalog.schema.table`.
  Without `--schema`, every schema except `information_schema` and `system` is read.
- snake_case names become PascalCase classes and properties, with `HasColumnName`; table and column
  comments become `HasComment` and XML documentation.
- **Every table scaffolds keyless** (`HasNoKey()`), because Trino exposes no keys. Add `HasKey` to the
  tables you write to.
- Columns of unmapped types (`array`, `map`, `row`, …) are skipped with a
  `TrinoEventId.ColumnSkipped` warning naming each one.

## Differences from SQL Server

| SQL Server | Trino provider |
|---|---|
| Identity and sequence keys | Keys set in .NET; `Guid` keys get UUIDv7 values |
| `SaveChanges` is atomic | Each statement commits on its own (not atomic) |
| Transactions | Not supported |
| `rowversion` concurrency tokens | A `long Version` you maintain |
| Unique indexes are enforced | Not enforced (warning) |
| Migrations | Not supported; `EnsureCreated` for development, scaffolding for existing tables |
| `UPDATE … FROM` | `MERGE` |
| Case-insensitive collation by default | Binary, case-sensitive comparison; case-insensitive identifiers |
| `NULL` first when ascending | Same (the provider adds `NULLS FIRST`) |
| `ROUND` half away from zero | `Math.Round` follows .NET (half to even) |
| `DATEDIFF` counts boundaries | `DateDiff*` counts whole units |
| One query = one round trip | One statement = one HTTP submission plus polling; ~0.3 s on a local single-node test coordinator |

## Performance tips

- **Batch writes.** Keep inserts into one table together in one `SaveChanges`: they are combined.
- **Prefer `ExecuteUpdate`/`ExecuteDelete`** over loading and saving entities: one statement instead
  of one per row.
- **Reuse connections.** Use `AddDbContextPool`, or share a `TrinoDataSource`, so contexts reuse
  HTTP connections. This matters most with TLS to a remote coordinator: in the tests, 20 queries
  through pooled contexts opened 1 TCP connection, and through new contexts 20.
- **Use `AsNoTracking`** for read-only queries: materialization then costs no more time than the
  raw `TrinoDataReader` (the server and transfer dominate) and allocates a third more, against twice
  as much with tracking.
- **Push work to Trino.** Filters, aggregation and `EF.Functions.ApproxDistinct` run in the cluster;
  avoid materializing large sets to aggregate in .NET.
- Statement latency dominates small operations. See the [benchmarks](benchmarks.md#9-ef-core-provider--ef7-t6).

## Diagnostics

The provider's events (use them with `ConfigureWarnings`, for example
`w.Throw(TrinoEventId.NonAtomicSaveChanges)`):

| Event | Category | Level | Meaning |
|---|---|---|---|
| `TrinoEventId.UniqueIndexNotEnforced` (30000) | `Model.Validation` | Warning | A unique index or alternate key is not enforced by Trino. |
| `TrinoEventId.NonAtomicSaveChanges` (30100) | `Update` | Warning | A `SaveChanges` is running more than one statement. |
| `TrinoEventId.ColumnSkipped` (30200) | `Scaffolding` | Warning | Scaffolding skipped a column of an unmapped type. |

Generated SQL is logged by EF's `RelationalEventId.CommandExecuting`
(`options.LogTo(Console.WriteLine, [RelationalEventId.CommandExecuting])`). Failures carry
`TrinoQueryException` as the inner exception, with Trino's `ErrorName`, `ErrorType`, query ID and
`IsTransient`. For connection and protocol problems, see [troubleshooting](troubleshooting.md).

## Testing against your cluster

The functional suite (`tests/TriQL.EntityFrameworkCore.FunctionalTests`) starts its own Trino
container. To check a real deployment (TLS, authentication, an object store, a multi-node cluster),
run its external lane against a writable catalog and schema:

```shell
RUN_EF_EXTERNAL=1 \
TRIQL_EF_EXTERNAL_CONNECTION_STRING="Server=https://trino.example.com;Catalog=lake;Schema=scratch;User=ci;Password=…" \
dotnet test tests/TriQL.EntityFrameworkCore.FunctionalTests --filter Category=EfExternal
```

It creates and drops one uniquely named table, and never creates or drops schemas.
