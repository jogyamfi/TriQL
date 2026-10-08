using Microsoft.EntityFrameworkCore.Query;
using TriQL.EntityFrameworkCore.Query.Internal.Translators;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Registers the Trino aggregate translators (<c>string.Join</c>, <c>ApproxDistinct</c>).</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoAggregateMethodCallTranslatorProvider : RelationalAggregateMethodCallTranslatorProvider
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoAggregateMethodCallTranslatorProvider(RelationalAggregateMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        AddTranslators([new TrinoAggregateMethodTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource)]);
    }
}
