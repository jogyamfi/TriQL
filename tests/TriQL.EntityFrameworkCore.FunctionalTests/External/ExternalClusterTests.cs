using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.External;

/// <summary>
/// EF8-T5: an opt-in smoke suite against a user-supplied cluster, used before releases to check a real
/// deployment (TLS, authentication, a non-local object store, a multi-node cluster) that the CI container
/// cannot reproduce. Skipped unless <c>RUN_EF_EXTERNAL=1</c>.
/// </summary>
/// <remarks>
/// <para>Environment variables:</para>
/// <list type="bullet">
/// <item><description><c>TRIQL_EF_EXTERNAL_CONNECTION_STRING</c> (required): a TriQL connection string whose
/// <c>Catalog</c> and <c>Schema</c> name a writable schema, typically on Iceberg, including any credentials,
/// e.g. <c>Server=https://trino.example.com;Catalog=lake;Schema=scratch;User=ci;Password=…</c>.</description></item>
/// </list>
/// <para>
/// The suite creates one uniquely named table (<c>efct_ext_&lt;id&gt;</c>) with <c>EnsureCreated</c> and drops
/// it with <c>EnsureDeleted</c>; it never creates or drops schemas. Run it with
/// <c>RUN_EF_EXTERNAL=1 dotnet test tests/TriQL.EntityFrameworkCore.FunctionalTests --filter Category=EfExternal</c>.
/// </para>
/// </remarks>
[Trait("Category", "EfExternal")]
public sealed class ExternalClusterTests(ITestOutputHelper output)
{
    private static readonly string TableName = $"efct_ext_{Guid.NewGuid():N}"[..24];

    [ExternalFact]
    public async Task CanConnect()
    {
        await using var db = CreateContext();
        Assert.True(await db.Database.CanConnectAsync());
    }

    [ExternalFact]
    public async Task Lifecycle_SaveChanges_Bulk_Concurrency()
    {
        await using (var db = CreateContext())
        {
            Assert.True(await db.Database.EnsureCreatedAsync());
        }

        try
        {
            await using (var db = CreateContext())
            {
                db.Items.AddRange(Enumerable.Range(1, 50).Select(i => new ExternalItem { Id = i, Label = $"item {i}" }));
                Assert.Equal(50, await db.SaveChangesAsync());
            }

            await using (var db = CreateContext())
            {
                Assert.Equal(50, await db.Items.CountAsync());
                Assert.Equal(10, await db.Items.Where(i => i.Id <= 10).ExecuteUpdateAsync(s => s.SetProperty(i => i.Label, "updated")));
                Assert.Equal(5, await db.Items.Where(i => i.Id > 45).ExecuteDeleteAsync());

                var item = await db.Items.SingleAsync(i => i.Id == 20);
                item.Label = "edited";
                item.Version++;
                await db.SaveChangesAsync();
            }

            await using (var first = CreateContext())
            await using (var second = CreateContext())
            {
                var a = await first.Items.SingleAsync(i => i.Id == 30);
                var b = await second.Items.SingleAsync(i => i.Id == 30);
                a.Version++;
                await first.SaveChangesAsync();
                b.Version++;
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            }

            await using (var db = CreateContext())
            {
                Assert.Equal(45, await db.Items.CountAsync());
                Assert.Equal(10, await db.Items.CountAsync(i => i.Label == "updated"));
                Assert.Equal("edited", await db.Items.Where(i => i.Id == 20).Select(i => i.Label).SingleAsync());
            }
        }
        finally
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private ExternalContext CreateContext() =>
        new(new DbContextOptionsBuilder<ExternalContext>()
            .UseTrino(Environment.GetEnvironmentVariable(ExternalFactAttribute.ConnectionStringVariable)!, o => o.EnableRetryOnFailure())
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options);

    public sealed class ExternalContext(DbContextOptions<ExternalContext> options) : DbContext(options)
    {
        public DbSet<ExternalItem> Items => Set<ExternalItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ExternalItem>(e =>
            {
                e.ToTable(TableName);
                e.Property(x => x.Version).IsConcurrencyToken();
            });
    }

    public sealed class ExternalItem
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public long Version { get; set; }
    }
}

/// <summary>A fact that runs only when <c>RUN_EF_EXTERNAL=1</c> and a connection string is supplied.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ExternalFactAttribute : FactAttribute
{
    /// <summary>The environment variable holding the external cluster's connection string.</summary>
    public const string ConnectionStringVariable = "TRIQL_EF_EXTERNAL_CONNECTION_STRING";

    public ExternalFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_EF_EXTERNAL") != "1")
        {
            Skip = "External-cluster lane: set RUN_EF_EXTERNAL=1 and " + ConnectionStringVariable + " to run.";
        }
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = ConnectionStringVariable + " is not set.";
        }
    }
}
