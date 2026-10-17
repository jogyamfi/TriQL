using Microsoft.EntityFrameworkCore.Metadata;
using TriQL.EntityFrameworkCore.Metadata.Internal;

// Extension methods live in EF's namespace, next to the other providers' entity type extensions.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Trino-specific extension methods for entity types.</summary>
public static class TrinoEntityTypeExtensions
{
    /// <summary>
    /// Returns the catalog of the entity type's table or view: its own (<c>HasCatalog</c>), else that of
    /// its base type, else that of its owner (for an owned type), else the model's default
    /// (<c>HasDefaultCatalog</c>), else <see langword="null"/>, meaning the connection's catalog.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The catalog, or <see langword="null"/>.</returns>
    public static string? GetCatalog(this IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        for (var type = entityType; type is not null; type = type.BaseType ?? type.FindOwnership()?.PrincipalEntityType)
        {
            if (type[TrinoAnnotationNames.Catalog] is string catalog)
            {
                return catalog;
            }
        }

        return entityType.Model.GetDefaultCatalog();
    }

    /// <summary>Sets the catalog of the entity type's table or view.</summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="catalog">The catalog, or <see langword="null"/> to use the model's default.</param>
    public static void SetCatalog(this IMutableEntityType entityType, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        entityType.SetOrRemoveAnnotation(TrinoAnnotationNames.Catalog, TrinoModelExtensions.NullIfEmpty(catalog));
    }

    /// <summary>
    /// The catalog of a table or view in the relational model: that of the entity types mapped to it
    /// (the model validator ensures they agree).
    /// </summary>
    internal static string? GetCatalog(this ITableBase table) =>
        table.EntityTypeMappings.Select(m => m.TypeBase).OfType<IReadOnlyEntityType>().Select(GetCatalog).FirstOrDefault(c => c is not null);
}
