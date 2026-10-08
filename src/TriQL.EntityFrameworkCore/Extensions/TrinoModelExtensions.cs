using Microsoft.EntityFrameworkCore.Metadata;
using TriQL.EntityFrameworkCore.Metadata.Internal;

// Extension methods live in EF's namespace, next to the other providers' model extensions.
namespace Microsoft.EntityFrameworkCore;

/// <summary>Trino-specific extension methods for <see cref="IReadOnlyModel"/> and <see cref="IMutableModel"/>.</summary>
public static class TrinoModelExtensions
{
    /// <summary>
    /// Returns the catalog of every table and view that does not set its own, or <see langword="null"/>
    /// when names resolve against the connection's catalog.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>The default catalog, or <see langword="null"/>.</returns>
    public static string? GetDefaultCatalog(this IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return (string?)model[TrinoAnnotationNames.DefaultCatalog];
    }

    /// <summary>Sets the catalog of every table and view that does not set its own.</summary>
    /// <param name="model">The model.</param>
    /// <param name="catalog">The catalog, or <see langword="null"/> to use the connection's catalog.</param>
    public static void SetDefaultCatalog(this IMutableModel model, string? catalog)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.SetOrRemoveAnnotation(TrinoAnnotationNames.DefaultCatalog, NullIfEmpty(catalog));
    }

    internal static string? NullIfEmpty(string? catalog) => string.IsNullOrWhiteSpace(catalog) ? null : catalog;
}
