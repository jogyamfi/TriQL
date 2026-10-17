// TriQL.EntityFrameworkCore quick start (EF10-T3). Reads the coordinator from TRINO_SERVER (default
// http://localhost:8080/) and, for the write examples, a catalog that supports UPDATE, DELETE and MERGE
// from TRINO_WRITE_CATALOG (default "iceberg"). Run: `TRINO_SERVER=https://host:8443 dotnet run`.
using Microsoft.EntityFrameworkCore;
using TriQL.Samples.EntityFrameworkCore;

var server = Environment.GetEnvironmentVariable("TRINO_SERVER") ?? "http://localhost:8080/";
var writeCatalog = Environment.GetEnvironmentVariable("TRINO_WRITE_CATALOG") ?? "iceberg";

Console.WriteLine("== LINQ over tpch.tiny (a scaffolded, keyless context) ==");
await QueryAsync(server);

Console.WriteLine();
Console.WriteLine($"== SaveChanges, concurrency and bulk operations on '{writeCatalog}' ==");
await WriteAsync(server, writeCatalog);

return;

// TpchContext is shaped like the output of
//   dotnet ef dbcontext scaffold "Server=…;Catalog=tpch;Schema=tiny" TriQL.EntityFrameworkCore
// keyless entities, PascalCase names mapped to Trino's lower-case columns.
static async Task QueryAsync(string server)
{
    await using var db = new TpchContext(new DbContextOptionsBuilder<TpchContext>()
        .UseTrino($"Server={server};User=triql-sample;Catalog=tpch;Schema=tiny")
        .Options);

    var byRegion = await db.Nations
        .Join(db.Regions, n => n.Regionkey, r => r.Regionkey, (n, r) => new { Region = r.Name, Nation = n.Name })
        .GroupBy(x => x.Region)
        .Select(g => new { Region = g.Key, Nations = string.Join(", ", g.OrderBy(x => x.Nation).Select(x => x.Nation)) })
        .OrderBy(x => x.Region)
        .ToListAsync();

    foreach (var region in byRegion)
    {
        Console.WriteLine($"  {region.Region}: {region.Nations}");
    }
}

static async Task WriteAsync(string server, string catalog)
{
    var options = new DbContextOptionsBuilder<StoreContext>()
        .UseTrino($"Server={server};User=triql-sample;Catalog={catalog};Schema={StoreContext.SchemaName}", o => o.EnableRetryOnFailure())
        .Options;

    await using (var db = new StoreContext(options))
    {
        // Development only: creates the schema and tables. Migrations are not supported.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Guid keys are generated in .NET (time-ordered UUIDv7); the three inserts become one INSERT.
        db.Products.AddRange(
            new Product { Name = "Kettle", Price = 39.90m },
            new Product { Name = "Toaster", Price = 24.50m },
            new Product { Name = "Mug", Price = 6.00m });
        Console.WriteLine($"  Inserted {await db.SaveChangesAsync()} products.");
    }

    await using (var db = new StoreContext(options))
    await using (var other = new StoreContext(options))
    {
        // Optimistic concurrency with a Version token the application maintains.
        var kettle = await db.Products.SingleAsync(p => p.Name == "Kettle");
        var sameKettle = await other.Products.SingleAsync(p => p.Name == "Kettle");

        kettle.Price = 34.90m;
        kettle.Version++;
        await db.SaveChangesAsync();

        sameKettle.Price = 44.90m;
        sameKettle.Version++;
        try
        {
            await other.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            Console.WriteLine("  The second edit of the kettle was rejected: another context changed it first.");
        }
    }

    await using (var db = new StoreContext(options))
    {
        // One UPDATE and one DELETE statement, without loading entities.
        var discounted = await db.Products.Where(p => p.Price > 20).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 0.9m));
        var removed = await db.Products.Where(p => p.Price < 10).ExecuteDeleteAsync();
        Console.WriteLine($"  Discounted {discounted} products and removed {removed}.");

        foreach (var product in await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync())
        {
            Console.WriteLine($"  {product.Name}: {product.Price:0.00} (version {product.Version})");
        }

        // Drops only the model's tables; the schema is left in place.
        await db.Database.EnsureDeletedAsync();
    }
}
