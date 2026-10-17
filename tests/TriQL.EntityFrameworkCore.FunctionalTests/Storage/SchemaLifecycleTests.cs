using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Storage;

/// <summary>
/// Phase 8 (EF8-T1) live: <c>EnsureCreated</c> creates the schema and tables (with every mapped type) on
/// Iceberg and on the memory catalog, <c>SaveChanges</c> round-trips every type through them, and
/// <c>EnsureDeleted</c> drops only the model's tables, keeping the schema and any other table in it.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class SchemaLifecycleTests(TrinoContainerFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _schema = $"efct_lifecycle_{Guid.NewGuid():N}"[..30];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var catalog in new[] { TrinoContainerFixture.IcebergCatalog, "memory" })
        {
            await using var db = new TypesContext(Options(catalog), catalog, _schema);
            await db.Database.EnsureDeletedAsync();
            await DdlAsync(db, $"DROP TABLE IF EXISTS {catalog}.{_schema}.unrelated");
            await DdlAsync(db, $"DROP SCHEMA IF EXISTS {catalog}.{_schema}");
        }
    }

    [Theory]
    [Trait("Category", "EfIceberg")]
    [InlineData("iceberg")]
    [InlineData("memory")]
    public async Task EnsureCreated_SaveChanges_EnsureDeleted(string catalog)
    {
        var expected = Sample();

        await using (var db = new TypesContext(Options("tpch"), catalog, _schema))
        {
            Assert.True(await db.Database.EnsureCreatedAsync());
            Assert.False(await db.Database.EnsureCreatedAsync());
            await DdlAsync(db, $"CREATE TABLE {catalog}.{_schema}.unrelated (x integer)");

            db.Rows.Add(expected);
            await db.SaveChangesAsync();
        }

        await using (var db = new TypesContext(Options("tpch"), catalog, _schema))
        {
            var actual = await db.Rows.AsNoTracking().SingleAsync();
            Assert.Equivalent(expected, actual, strict: true);

            Assert.True(await db.Database.EnsureDeletedAsync());
            Assert.False(await db.Database.EnsureDeletedAsync());

            // The schema and the table EF does not own are still there.
            var schema = _schema;
            var remaining = catalog == "memory"
                ? await db.Database.SqlQuery<string>($"SELECT table_name AS \"Value\" FROM memory.information_schema.tables WHERE table_schema = {schema}").ToListAsync()
                : await db.Database.SqlQuery<string>($"SELECT table_name AS \"Value\" FROM iceberg.information_schema.tables WHERE table_schema = {schema}").ToListAsync();
            Assert.Equal(["unrelated"], remaining);
        }
    }

    private static TypeRow Sample() => new()
    {
        Id = Guid.CreateVersion7(),
        Flag = true,
        Tiny = -12,
        Byte = 250,
        Small = -30_000,
        Count = int.MinValue,
        BigInt = long.MaxValue,
        Real = 1.5f,
        Ratio = Math.PI,
        Money = 12345.6789m,
        Text = "it's ünïcödé",
        NullableText = null,
        Letter = 'q',
        Bytes = [0, 1, 255],
        Day = new DateOnly(2024, 2, 29),
        Time = new TimeOnly(23, 59, 59, 999),
        Stamp = new DateTime(2026, 10, 8, 16, 30, 0).AddTicks(1_234_560),
        Instant = new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero).AddTicks(1_234_560),
        Duration = TimeSpan.FromHours(-3.25),
        Rating = null,
    };

    // DDL with generated identifiers (never user input), so raw SQL is safe here.
    private static Task<int> DdlAsync(DbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    // Connected to another catalog: every name the model uses is three-part.
    private DbContextOptions<TypesContext> Options(string connectionCatalog) =>
        new DbContextOptionsBuilder<TypesContext>()
            .UseTrino($"Server={fixture.ServerUri};User=triql-ef;Catalog={connectionCatalog};Schema=tiny")
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PerCatalogModelCacheKeyFactory>()
            .Options;

    public sealed class TypesContext(DbContextOptions<TypesContext> options, string catalog, string schema) : DbContext(options)
    {
        public string Catalog { get; } = catalog;

        public string Schema { get; } = schema;

        public DbSet<TypeRow> Rows => Set<TypeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultCatalog(Catalog).HasDefaultSchema(Schema);
            modelBuilder.Entity<TypeRow>(e =>
            {
                e.ToTable("type_rows", t => t.HasComment("Every mapped type"));
                e.Property(x => x.Money).HasPrecision(18, 4).HasComment("decimal(18,4)");
            });
        }
    }

    private sealed class PerCatalogModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((TypesContext)context).Catalog, ((TypesContext)context).Schema, designTime);
    }

    public sealed class TypeRow
    {
        public Guid Id { get; set; }

        public bool Flag { get; set; }

        public sbyte Tiny { get; set; }

        public byte Byte { get; set; }

        public short Small { get; set; }

        public int Count { get; set; }

        public long BigInt { get; set; }

        public float Real { get; set; }

        public double Ratio { get; set; }

        public decimal Money { get; set; }

        public string Text { get; set; } = string.Empty;

        public string? NullableText { get; set; }

        public char Letter { get; set; }

        public byte[] Bytes { get; set; } = [];

        public DateOnly Day { get; set; }

        public TimeOnly Time { get; set; }

        public DateTime Stamp { get; set; }

        public DateTimeOffset Instant { get; set; }

        public TimeSpan Duration { get; set; }

        public int? Rating { get; set; }
    }
}
