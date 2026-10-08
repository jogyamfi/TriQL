using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Update;

/// <summary>
/// Phase 7 live suite on the fixture's Iceberg catalog: <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> (including the
/// <c>MERGE</c> generated for updates that join other tables) with their affected-row counts, multi-row inserts,
/// three-part names in DML, and <c>ExecuteUpdate</c> retried as a whole after <c>ICEBERG_COMMIT_ERROR</c>.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfIceberg")]
public sealed class IcebergBulkOperationTests(TrinoContainerFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _schema = $"ef_bulk_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await DdlAsync(db, $"CREATE SCHEMA {TrinoContainerFixture.IcebergCatalog}.{_schema}");
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            var columns = string.Join(", ", table.Columns.Select(c => $"\"{c.Name}\" {c.StoreType}"));
            await DdlAsync(db, $"CREATE TABLE \"{table.Name}\" ({columns})");
        }
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            await DdlAsync(db, $"DROP TABLE IF EXISTS \"{table.Name}\"");
        }

        await DdlAsync(db, $"DROP SCHEMA IF EXISTS {TrinoContainerFixture.IcebergCatalog}.{_schema}");
    }

    [Fact]
    public async Task ExecuteDelete_Shapes_DeleteTheRightRows()
    {
        await SeedAsync();
        await using var db = CreateContext();

        var simple = await db.Items.Where(i => i.Score < 2).ExecuteDeleteAsync();
        var related = await db.Items.Where(i => db.Groups.Any(g => g.Id == i.GroupId && g.Name == "B")).ExecuteDeleteAsync();
        var paged = await db.Items.OrderByDescending(i => i.Id).Take(2).ExecuteDeleteAsync();

        Assert.Equal((1, 3, 2), (simple, related, paged));
        Assert.Equal([2, 6], await db.Items.OrderBy(i => i.Id).Select(i => i.Id).ToListAsync());
    }

    [Fact]
    public async Task ExecuteUpdate_SingleTable_AndMergeShapes_UpdateTheRightRows()
    {
        await SeedAsync();
        await using var db = CreateContext();

        var simple = await db.Items.Where(i => i.GroupId == 1).ExecuteUpdateAsync(s => s.SetProperty(i => i.Score, i => i.Score * 10));
        var fromJoin = await db.Items.Join(db.Groups, i => i.GroupId, g => g.Id, (i, g) => new { i, g })
            .Where(x => x.g.Name == "B")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.i.Label, x => x.g.Name + "-" + x.i.Label));
        var paged = await db.Items.OrderBy(i => i.Id).Take(2).ExecuteUpdateAsync(s => s.SetProperty(i => i.Label, "first"));

        Assert.Equal((5, 3, 2), (simple, fromJoin, paged));
        var items = await db.Items.OrderBy(i => i.Id).Select(i => new { i.Id, i.Score, i.Label }).ToListAsync();
        Assert.Equal(
            Seed().Select(i => new
            {
                i.Id,
                Score = i.GroupId == 1 ? i.Score * 10 : i.Score,
                Label = i.Id <= 2 ? "first" : i.GroupId == 2 ? "B-" + i.Label : i.Label,
            }),
            items);
    }

    [Fact]
    public async Task ThreePartNames_WorkInDml_FromAnotherCatalog()
    {
        await SeedAsync();
        await using var db = new CatalogBulkContext(Options<CatalogBulkContext>("Catalog=memory;Schema=default"), TrinoContainerFixture.IcebergCatalog, _schema);

        var updated = await db.Items.Where(i => i.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(i => i.Label, "remote"));
        var deleted = await db.Items.Where(i => i.Id == 8).ExecuteDeleteAsync();
        db.Items.Add(new Item { Id = 100, GroupId = 1, Label = "added" });
        await db.SaveChangesAsync();

        Assert.Equal((1, 1), (updated, deleted));
        await using var check = CreateContext();
        Assert.Equal("remote", await check.Items.Where(i => i.Id == 1).Select(i => i.Label).SingleAsync());
        Assert.Equal(8, await check.Items.CountAsync());
    }

    [Fact]
    public async Task ManyInserts_AreCombined_AndAllRowsArrive()
    {
        const int rows = 2_500;
        var statements = 0;
        await using (var db = CreateContext(onCommand: _ => Interlocked.Increment(ref statements)))
        {
            db.Items.AddRange(Enumerable.Range(1, rows).Select(i => new Item { Id = i, GroupId = i % 3, Score = i, Label = $"row {i}" }));
            Assert.Equal(rows, await db.SaveChangesAsync());
        }

        // 4 parameters per row: 500 rows fill the 2000-parameter budget, so 5 statements instead of 2500.
        Assert.Equal(5, statements);
        await using var check = CreateContext();
        Assert.Equal((rows, (long)rows * (rows + 1) / 2), (await check.Items.CountAsync(), await check.Items.SumAsync(i => (long)i.Score)));
    }

    [Fact]
    public async Task ConcurrentExecuteUpdates_AreRetriedAsAWhole_WithRetryOnFailure()
    {
        await SeedAsync();
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(async id =>
        {
            await using var db = CreateContext(retry: true);
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                db.Items.Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.Score, i => i.Score + 1000)));
        }));

        Assert.All(results, r => Assert.Equal(1, r));
        await using var check = CreateContext();
        Assert.Equal(Seed().Select(i => i.Score + 1000), await check.Items.OrderBy(i => i.Id).Select(i => i.Score).ToListAsync());
    }

    // ---- helpers --------------------------------------------------------------------------

    private static List<Item> Seed() =>
    [
        new() { Id = 1, GroupId = 1, Score = 1, Label = "a" },
        new() { Id = 2, GroupId = 1, Score = 2, Label = "b" },
        new() { Id = 3, GroupId = 2, Score = 3, Label = "c" },
        new() { Id = 4, GroupId = 2, Score = 4, Label = "d" },
        new() { Id = 5, GroupId = 2, Score = 5, Label = "e" },
        new() { Id = 6, GroupId = 1, Score = 6, Label = "f" },
        new() { Id = 7, GroupId = 1, Score = 7, Label = "g" },
        new() { Id = 8, GroupId = 1, Score = 8, Label = "h" },
    ];

    private async Task SeedAsync()
    {
        await using var db = CreateContext();
        db.Groups.AddRange(new Group { Id = 1, Name = "A" }, new Group { Id = 2, Name = "B" });
        db.Items.AddRange(Seed());
        await db.SaveChangesAsync();
    }

    // DDL with generated identifiers (never user input), so raw SQL is safe here.
    private static Task<int> DdlAsync(DbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    private BulkContext CreateContext(bool retry = false, Action<string>? onCommand = null) =>
        new(Options<BulkContext>($"Catalog={TrinoContainerFixture.IcebergCatalog};Schema={_schema}", retry, onCommand));

    private DbContextOptions<TContext> Options<TContext>(string scope, bool retry = false, Action<string>? onCommand = null)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseTrino($"Server={fixture.ServerUri};User=triql-ef;{scope}", o =>
            {
                if (retry)
                {
                    o.EnableRetryOnFailure(10, TimeSpan.FromSeconds(2));
                }
            })
            .LogTo(
                (id, _) => id == RelationalEventId.CommandExecuting,
                e =>
                {
                    var sql = ((CommandEventData)e).Command.CommandText;
                    onCommand?.Invoke(sql);
                    output.WriteLine(sql);
                })
            .Options;

    public sealed class BulkContext(DbContextOptions<BulkContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        public DbSet<Group> Groups => Set<Group>();
    }

    /// <summary>The same tables, named with three parts from a connection to another catalog. One schema per test run, so model caching is safe.</summary>
    public sealed class CatalogBulkContext(DbContextOptions<CatalogBulkContext> options, string catalog, string schema) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();

        public DbSet<Group> Groups => Set<Group>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasDefaultCatalog(catalog).HasDefaultSchema(schema);
    }

    public sealed class Item
    {
        public int Id { get; set; }

        public int GroupId { get; set; }

        public int Score { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public sealed class Group
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
