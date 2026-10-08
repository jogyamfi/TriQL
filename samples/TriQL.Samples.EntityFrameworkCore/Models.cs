using Microsoft.EntityFrameworkCore;

namespace TriQL.Samples.EntityFrameworkCore;

/// <summary>A read-only context over tpch.tiny, in the shape <c>dotnet ef dbcontext scaffold</c> produces.</summary>
public sealed class TpchContext(DbContextOptions<TpchContext> options) : DbContext(options)
{
    public DbSet<Nation> Nations => Set<Nation>();

    public DbSet<Region> Regions => Set<Region>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Trino exposes no keys, so scaffolded tables are keyless; add HasKey for tables you write to.
        modelBuilder.Entity<Nation>(entity =>
        {
            entity.HasNoKey().ToTable("nation");
            entity.Property(e => e.Nationkey).HasColumnName("nationkey");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Regionkey).HasColumnName("regionkey");
        });

        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasNoKey().ToTable("region");
            entity.Property(e => e.Regionkey).HasColumnName("regionkey");
            entity.Property(e => e.Name).HasColumnName("name");
        });
    }
}

public sealed class Nation
{
    public long Nationkey { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Regionkey { get; set; }
}

public sealed class Region
{
    public long Regionkey { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>A writable context: keys and concurrency tokens are managed in .NET.</summary>
public sealed class StoreContext(DbContextOptions<StoreContext> options) : DbContext(options)
{
    public const string SchemaName = "triql_sample";

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(SchemaName).Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(10, 2);
            entity.Property(p => p.Version).IsConcurrencyToken();
        });
}

public sealed class Product
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long Version { get; set; }
}
