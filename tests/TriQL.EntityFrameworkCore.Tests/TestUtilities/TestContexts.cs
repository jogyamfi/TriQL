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
