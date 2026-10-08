using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.Client.Exceptions;
using TriQL.EntityFrameworkCore.Infrastructure;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Update;

/// <summary>
/// Phase 6 live suite on the fixture's Iceberg catalog: <c>SaveChanges</c> CRUD, optimistic concurrency
/// between two contexts, the <c>NULL</c> row count of a metadata-only delete, and per-statement retries of
/// the <c>ICEBERG_COMMIT_ERROR</c> that concurrent writers to one table cause. Each test class run gets its
/// own schema, dropped afterwards.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfIceberg")]
public sealed class IcebergSaveChangesTests(TrinoContainerFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _schema = $"ef_save_{Guid.NewGuid():N}";
    private readonly List<string> _tables = [];

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await DdlAsync(db, $"CREATE SCHEMA {TrinoContainerFixture.IcebergCatalog}.{_schema}");
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        foreach (var table in _tables)
        {
            await DdlAsync(db, $"DROP TABLE IF EXISTS \"{table}\"");
        }

        await DdlAsync(db, $"DROP SCHEMA IF EXISTS {TrinoContainerFixture.IcebergCatalog}.{_schema}");
    }

    [Fact]
    public async Task Crud_RoundTrip()
    {
        await CreateTablesAsync();
        var created = new DateTime(2026, 10, 8, 12, 30, 45, 123).AddTicks(4560);

        await using (var db = CreateContext())
        {
            db.Accounts.AddRange(
                new Account { Id = 1, Owner = "ann", Balance = 10.50m, Updated = created },
                new Account { Id = 2, Owner = "bob", Balance = -3.25m, Updated = created });
            db.Notes.Add(new Note { Text = "hello" });
            Assert.Equal(3, await db.SaveChangesAsync());
        }

        await using (var db = CreateContext())
        {
            var ann = await db.Accounts.SingleAsync(a => a.Id == 1);
            Assert.Equal(("ann", 10.50m, created), (ann.Owner, ann.Balance, ann.Updated));
            ann.Balance += 1;
            ann.Version++;
            db.Accounts.Remove(await db.Accounts.SingleAsync(a => a.Id == 2));
            var note = await db.Notes.SingleAsync();
            Assert.Equal(7, note.Id.Version);
            note.Text = "edited";
            Assert.Equal(3, await db.SaveChangesAsync());
        }

        await using (var db = CreateContext())
        {
            var accounts = await db.Accounts.AsNoTracking().ToListAsync();
            var account = Assert.Single(accounts);
            Assert.Equal((1, 11.50m, 1L), (account.Id, account.Balance, account.Version));
            Assert.Equal("edited", (await db.Notes.SingleAsync()).Text);
        }
    }

    [Fact]
    public async Task ConcurrentEdit_FromTwoContexts_ThrowsConcurrencyException_ForTheSecond()
    {
        await CreateTablesAsync();
        await using (var seed = CreateContext())
        {
            seed.Accounts.Add(new Account { Id = 1, Owner = "ann", Balance = 100m });
            await seed.SaveChangesAsync();
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        var a1 = await first.Accounts.SingleAsync();
        var a2 = await second.Accounts.SingleAsync();

        a1.Balance -= 30;
        a1.Version++;
        await first.SaveChangesAsync();

        a2.Balance -= 50;
        a2.Version++;
        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The usual resolution: reload the current values and apply the change again.
        await conflict.Entries.Single().ReloadAsync();
        a2.Balance -= 50;
        a2.Version++;
        await second.SaveChangesAsync();

        await using var check = CreateContext();
        Assert.Equal((20m, 2L), await check.Accounts.Select(a => ValueTuple.Create(a.Balance, a.Version)).SingleAsync());
    }

    [Fact]
    public async Task DeletingARowThatIsAlreadyGone_FromAPartitionedTable_IsAConcurrencyConflict()
    {
        // Partitioned by the key, so a key-based DELETE is a metadata-only delete; when it matches nothing,
        // Trino reports the row count as NULL (measured on 466), which must still be read as 0 rows.
        await CreateTablesAsync(accountPartitioning: "WITH (partitioning = ARRAY['Id'])");
        await using (var seed = CreateContext())
        {
            seed.Accounts.Add(new Account { Id = 5, Owner = "eve" });
            await seed.SaveChangesAsync();
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        first.Accounts.Remove(await first.Accounts.SingleAsync());
        second.Accounts.Remove(await second.Accounts.SingleAsync());

        Assert.Equal(1, await first.SaveChangesAsync());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentWriters_ConflictWithIcebergCommitError_AndAreRetried()
    {
        const int writers = 10;
        await CreateTablesAsync();
        await using (var seed = CreateContext())
        {
            seed.Accounts.AddRange(Enumerable.Range(1, writers).Select(i => new Account { Id = i, Owner = $"o{i}" }));
            await seed.SaveChangesAsync();
        }

        var statements = 0;
        var failures = await Task.WhenAll(Enumerable.Range(1, writers).Select(async i =>
        {
            await using var db = CreateContext(
                retry: true,
                onCommand: sql =>
                {
                    if (sql.StartsWith("UPDATE", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref statements);
                    }
                });
            var account = await db.Accounts.SingleAsync(a => a.Id == i);
            account.Balance = i;
            account.Version++;
            try
            {
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex)
            {
                return ex;
            }
        }));

        output.WriteLine($"{writers} concurrent updates took {statements} UPDATE statements.");
        Assert.All(failures, Assert.Null);
        Assert.True(statements > writers, "Expected at least one ICEBERG_COMMIT_ERROR to be retried.");

        await using var check = CreateContext();
        Assert.Equal(
            Enumerable.Range(1, writers).Select(i => ((decimal)i, 1L)),
            await check.Accounts.OrderBy(a => a.Id).Select(a => ValueTuple.Create(a.Balance, a.Version)).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentWriters_WithoutRetries_FailWithIcebergCommitError()
    {
        const int writers = 10;
        await CreateTablesAsync();
        await using (var seed = CreateContext())
        {
            seed.Accounts.AddRange(Enumerable.Range(1, writers).Select(i => new Account { Id = i, Owner = $"o{i}" }));
            await seed.SaveChangesAsync();
        }

        var failures = await Task.WhenAll(Enumerable.Range(1, writers).Select(async i =>
        {
            await using var db = CreateContext();
            var account = await db.Accounts.SingleAsync(a => a.Id == i);
            account.Balance = i;
            try
            {
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex)
            {
                return ex;
            }
        }));

        // Iceberg commits optimistically; concurrent row-level updates of one table conflict.
        var errors = failures.OfType<DbUpdateException>().ToList();
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal("ICEBERG_COMMIT_ERROR", Assert.IsType<TrinoQueryException>(e.InnerException).ErrorName));
    }

    [Fact]
    public async Task ExecuteSql_ReturnsRowsAffected()
    {
        await CreateTablesAsync();
        await using var db = CreateContext();
        db.Accounts.AddRange(Enumerable.Range(1, 3).Select(i => new Account { Id = i, Owner = "x" }));
        await db.SaveChangesAsync();

        var updated = await db.Database.ExecuteSqlAsync($"UPDATE \"Accounts\" SET \"Owner\" = {"y"} WHERE \"Id\" >= {2}");

        Assert.Equal(2, updated);
    }

    // ---- helpers --------------------------------------------------------------------------

    // DDL with generated identifiers (never user input), so raw SQL is safe here.
    private static Task<int> DdlAsync(DbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    private SaveContext CreateContext(bool retry = false, Action<string>? onCommand = null) =>
        new(new DbContextOptionsBuilder<SaveContext>()
            .UseTrino(
                $"Server={fixture.ServerUri};User=triql-ef;Catalog={TrinoContainerFixture.IcebergCatalog};Schema={_schema}",
                o =>
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
            .Options);

    private async Task CreateTablesAsync(string accountPartitioning = "")
    {
        await using var db = CreateContext();
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            var columns = string.Join(", ", table.Columns.Select(c => $"\"{c.Name}\" {c.StoreType}"));
            var properties = table.Name == "Accounts" ? accountPartitioning : string.Empty;
            await DdlAsync(db, $"CREATE TABLE \"{table.Name}\" ({columns}) {properties}");
            _tables.Add(table.Name);
        }
    }

    public sealed class SaveContext(DbContextOptions<SaveContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        public DbSet<Note> Notes => Set<Note>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(a =>
            {
                a.Property(x => x.Balance).HasPrecision(10, 2);
                a.Property(x => x.Version).IsConcurrencyToken();
            });
        }
    }

    public sealed class Account
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public decimal Balance { get; set; }

        public long Version { get; set; }

        public DateTime Updated { get; set; }
    }

    public sealed class Note
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
