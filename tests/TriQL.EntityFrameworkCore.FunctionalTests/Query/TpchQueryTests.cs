using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TriQL.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace TriQL.EntityFrameworkCore.FunctionalTests.Query;

/// <summary>
/// LINQ over the read-only <c>tpch.tiny</c> schema bundled with Trino: the connector's own types
/// (<c>bigint</c> keys, <c>double</c> money columns, <c>date</c>, <c>varchar(n)</c>) and data volumes,
/// mapped with explicit table and column names. Expected values are TPC-H facts for the tiny scale factor.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
[Trait("Category", "EfRead")]
public sealed class TpchQueryTests(TrinoContainerFixture fixture, ITestOutputHelper output)
{
    private TpchContext CreateContext() =>
        new(new DbContextOptionsBuilder<TpchContext>()
            .UseTrino($"Server={fixture.ServerUri};User=triql-ef;Catalog=tpch;Schema=tiny")
            .LogTo(output.WriteLine, [RelationalEventId.CommandExecuting])
            .Options);

    [Fact]
    public async Task Join_NationsToRegions_WithFilterAndOrdering()
    {
        await using var db = CreateContext();

        var actual = await db.Nations.Where(n => n.Region.Name == "EUROPE").OrderBy(n => n.Name).Select(n => n.Name).ToListAsync();

        Assert.Equal(["FRANCE", "GERMANY", "ROMANIA", "RUSSIA", "UNITED KINGDOM"], actual);
    }

    [Fact]
    public async Task Aggregates_OverDecimalAndDate()
    {
        await using var db = CreateContext();

        var count = await db.Orders.CountAsync();
        var earliest = await db.Orders.MinAsync(o => o.OrderDate);
        var total = await db.Orders.Where(o => o.OrderDate < new DateOnly(1993, 1, 1)).SumAsync(o => o.TotalPrice);

        Assert.Equal(15_000, count);
        Assert.Equal(new DateOnly(1992, 1, 1), earliest);
        Assert.True(total > 0d);
    }

    [Fact]
    public async Task GroupBy_OrderStatus_WithSumAndAverage()
    {
        await using var db = CreateContext();

        var actual = await db.Orders.GroupBy(o => o.OrderStatus)
            .Select(g => new { Status = g.Key, Count = g.Count(), Average = g.Average(o => o.TotalPrice) })
            .OrderBy(x => x.Status).ToListAsync();

        Assert.Equal(["F", "O", "P"], actual.Select(x => x.Status));
        Assert.Equal(15_000, actual.Sum(x => x.Count));
        Assert.All(actual, x => Assert.True(x.Average > 0d));
    }

    [Fact]
    public async Task TopCustomersPerNation_ViaCollectionProjectionWithTake()
    {
        await using var db = CreateContext();

        var actual = await db.Nations.OrderBy(n => n.NationKey).Take(3)
            .Select(n => new { n.Name, Top = n.Customers.OrderByDescending(c => c.AccountBalance).Take(2).Select(c => c.Name).ToList() })
            .ToListAsync();

        Assert.Equal(3, actual.Count);
        Assert.All(actual, x => Assert.Equal(2, x.Top.Count));
    }

    [Fact]
    public async Task StreamingALargeResult_ReadsEveryRow()
    {
        await using var db = CreateContext();
        var rows = 0;

        await foreach (var _ in db.Customers.AsNoTracking().AsAsyncEnumerable())
        {
            rows++;
        }

        Assert.Equal(1_500, rows);
    }
}

public sealed class TpchContext(DbContextOptions<TpchContext> options) : DbContext(options)
{
    public DbSet<Region> Regions => Set<Region>();

    public DbSet<Nation> Nations => Set<Nation>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>(e =>
        {
            e.ToTable("region").HasKey(r => r.RegionKey);
            e.Property(r => r.RegionKey).HasColumnName("regionkey");
            e.Property(r => r.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Nation>(e =>
        {
            e.ToTable("nation").HasKey(n => n.NationKey);
            e.Property(n => n.NationKey).HasColumnName("nationkey");
            e.Property(n => n.Name).HasColumnName("name");
            e.Property(n => n.RegionKey).HasColumnName("regionkey");
            e.HasOne(n => n.Region).WithMany().HasForeignKey(n => n.RegionKey);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("customer").HasKey(c => c.CustKey);
            e.Property(c => c.CustKey).HasColumnName("custkey");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.NationKey).HasColumnName("nationkey");
            e.Property(c => c.AccountBalance).HasColumnName("acctbal");
            e.HasOne<Nation>().WithMany(n => n.Customers).HasForeignKey(c => c.NationKey);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders").HasKey(o => o.OrderKey);
            e.Property(o => o.OrderKey).HasColumnName("orderkey");
            e.Property(o => o.CustKey).HasColumnName("custkey");
            e.Property(o => o.OrderStatus).HasColumnName("orderstatus");
            e.Property(o => o.TotalPrice).HasColumnName("totalprice");
            e.Property(o => o.OrderDate).HasColumnName("orderdate");
        });
    }
}

public sealed class Region
{
    public long RegionKey { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class Nation
{
    public long NationKey { get; set; }

    public string Name { get; set; } = string.Empty;

    public long RegionKey { get; set; }

    public Region Region { get; set; } = null!;

    public List<Customer> Customers { get; set; } = [];
}

public sealed class Customer
{
    public long CustKey { get; set; }

    public string Name { get; set; } = string.Empty;

    public long NationKey { get; set; }

    public double AccountBalance { get; set; }
}

public sealed class Order
{
    public long OrderKey { get; set; }

    public long CustKey { get; set; }

    public string OrderStatus { get; set; } = string.Empty;

    public double TotalPrice { get; set; }

    public DateOnly OrderDate { get; set; }
}
