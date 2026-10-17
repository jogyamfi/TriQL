using Microsoft.EntityFrameworkCore.Metadata.Builders;

// Extension methods live in EF's namespace, next to HasDefaultSchema and the other providers' builders.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Trino-specific model-building methods: the catalog of tables and views, for three-part names
/// (<c>"catalog"."schema"."table"</c>). A table in a catalog must also have a schema
/// (<c>ToTable(name, schema)</c> or <c>HasDefaultSchema</c>). Without a catalog, names resolve against
/// the connection's catalog (and, without a schema, its schema).
/// </summary>
public static class TrinoModelBuilderExtensions
{
    /// <summary>
    /// Sets the catalog of every table and view that does not set its own with <c>HasCatalog</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="catalog">The catalog, or <see langword="null"/> to use the connection's catalog.</param>
    /// <returns>The same builder, so that calls can be chained.</returns>
    public static ModelBuilder HasDefaultCatalog(this ModelBuilder modelBuilder, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Model.SetDefaultCatalog(catalog);
        return modelBuilder;
    }

    /// <summary>Sets the catalog of the entity type's table or view, overriding the model's default.</summary>
    /// <param name="entityTypeBuilder">The entity type builder.</param>
    /// <param name="catalog">The catalog, or <see langword="null"/> to use the model's default.</param>
    /// <returns>The same builder, so that calls can be chained.</returns>
    public static EntityTypeBuilder HasCatalog(this EntityTypeBuilder entityTypeBuilder, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);
        entityTypeBuilder.Metadata.SetCatalog(catalog);
        return entityTypeBuilder;
    }

    /// <summary>Sets the catalog of the entity type's table or view, overriding the model's default.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entityTypeBuilder">The entity type builder.</param>
    /// <param name="catalog">The catalog, or <see langword="null"/> to use the model's default.</param>
    /// <returns>The same builder, so that calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> HasCatalog<TEntity>(this EntityTypeBuilder<TEntity> entityTypeBuilder, string? catalog)
        where TEntity : class =>
        (EntityTypeBuilder<TEntity>)HasCatalog((EntityTypeBuilder)entityTypeBuilder, catalog);
}
