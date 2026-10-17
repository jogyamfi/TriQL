using Microsoft.EntityFrameworkCore;

namespace TriQL.EntityFrameworkCore.Tests.TestUtilities;

/// <summary>A context with no entity types, for raw SQL and infrastructure tests.</summary>
internal sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);

/// <summary>A context with one entity type, for tests that need a model.</summary>
internal sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
}

internal sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>A context whose entity has a property of every mapped CLR type, with facets.</summary>
internal sealed class AllTypesContext(DbContextOptions<AllTypesContext> options) : DbContext(options)
{
    public DbSet<AllTypes> AllTypes => Set<AllTypes>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AllTypes>();
        entity.Property(e => e.ShortString).HasMaxLength(10);
        entity.Property(e => e.FixedString).HasMaxLength(2).IsFixedLength();
        entity.Property(e => e.WideDecimal).HasPrecision(38, 10);
        entity.Property(e => e.MillisecondTimestamp).HasPrecision(3);
    }
}

internal enum Color
{
    Red,
    Green,
}

internal sealed class AllTypes
{
    public int Id { get; set; }

    public string? ShortString { get; set; }

    public string? FixedString { get; set; }

    public decimal WideDecimal { get; set; }

    public DateTime MillisecondTimestamp { get; set; }

    public Color Enum { get; set; }
}
