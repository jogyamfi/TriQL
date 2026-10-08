namespace TriQL.EntityFrameworkCore.Metadata.Internal;

/// <summary>Names of the model annotations the Trino provider defines.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public static class TrinoAnnotationNames
{
    /// <summary>The prefix of every Trino annotation name.</summary>
    public const string Prefix = "Trino:";

    /// <summary>The catalog of an entity type's table or view.</summary>
    public const string Catalog = Prefix + "Catalog";

    /// <summary>The catalog of every table and view that does not set its own.</summary>
    public const string DefaultCatalog = Prefix + "DefaultCatalog";
}
