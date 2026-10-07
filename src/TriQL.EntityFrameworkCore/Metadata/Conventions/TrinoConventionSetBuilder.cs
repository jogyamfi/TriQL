using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace TriQL.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Builds the model-building conventions for the Trino provider: EF's relational conventions
/// (table and column mapping). Phase 5 adds the Trino-specific ones, such as integer keys not
/// being treated as database-generated.
/// </summary>
public class TrinoConventionSetBuilder : RelationalConventionSetBuilder
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }
}
