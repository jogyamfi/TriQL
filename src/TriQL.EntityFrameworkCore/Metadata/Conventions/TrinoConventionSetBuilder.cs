using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace TriQL.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Builds the model-building conventions for the Trino provider: EF's relational conventions (table
/// and column mapping), with <see cref="TrinoValueGenerationConvention"/> in place of EF's value
/// generation convention, so that integer keys are not treated as database-generated.
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

    /// <inheritdoc />
    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();
        conventionSet.Replace<ValueGenerationConvention>(new TrinoValueGenerationConvention(Dependencies, RelationalDependencies));
        return conventionSet;
    }
}
