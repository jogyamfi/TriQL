using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// Phase 5 live check of three-part names: a context connected to the <c>memory</c> catalog (the
/// seeded Blogging schema) reads <c>tpch.tiny</c> through <c>HasCatalog</c>/<c>HasDefaultCatalog</c>, and
/// joins across the two catalogs in one query.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class CatalogFunctionalTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task HasCatalog_ReadsAnotherCatalog_AndJoinsAcrossCatalogs()
    {
        await using var db = new CrossCatalogContext(await OptionsAsync<CrossCatalogContext>());

        var nations = await db.Nations.CountAsync();
        var perBlog = await db.Blogs.OrderBy(b => b.Id)
            .Select(b => new { b.Name, Nations = db.Nations.Count(n => n.RegionKey == b.Id) })
            .ToListAsync();

        Assert.Equal(25, nations);
        Assert.Equal(BloggingDatabase.Blogs.OrderBy(b => b.Id).Select(b => new { b.Name, Nations = 5 }), perBlog);
    }

    [Fact]
    public async Task HasDefaultCatalog_AndDefaultSchema_ApplyToEveryTable()
    {
        await using var db = new TpchDefaultCatalogContext(await OptionsAsync<TpchDefaultCatalogContext>());

        var europe = await db.Nations.Where(n => n.RegionKey == 3).OrderBy(n => n.Name).Select(n => n.Name).ToListAsync();

        Assert.Equal(["FRANCE", "GERMANY", "ROMANIA", "RUSSIA", "UNITED KINGDOM"], europe);
    }

    private async Task<DbContextOptions<TContext>> OptionsAsync<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseTrino(await BloggingDatabase.GetConnectionStringAsync(fixture))
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options;

    private sealed class CrossCatalogContext(DbContextOptions<CrossCatalogContext> options) : DbContext(options)
    {
        public DbSet<BlogRow> Blogs => Set<BlogRow>();

        public DbSet<NationRow> Nations => Set<NationRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlogRow>().ToTable("Blogs");
            modelBuilder.Entity<NationRow>().HasKey(n => n.NationKey);
            modelBuilder.Entity<NationRow>().HasCatalog("tpch").ToTable("nation", "tiny");
        }
    }

    private sealed class TpchDefaultCatalogContext(DbContextOptions<TpchDefaultCatalogContext> options) : DbContext(options)
    {
        public DbSet<NationRow> Nations => Set<NationRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasDefaultCatalog("tpch").HasDefaultSchema("tiny").Entity<NationRow>().ToTable("nation").HasKey(n => n.NationKey);
    }

    private sealed class BlogRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    // Trino identifiers are case-insensitive, so PascalCase properties match tpch's lower-case columns.
    private sealed class NationRow
    {
        public long NationKey { get; set; }

        public string Name { get; set; } = string.Empty;

        public long RegionKey { get; set; }
    }
}
