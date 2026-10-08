using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Applies EF's null semantics to the query, including the provider's own SQL expressions.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoSqlNullabilityProcessor : SqlNullabilityProcessor
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    /// <inheritdoc />
    protected override SqlExpression VisitCustomSqlExpression(SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        if (sqlExpression is TrinoStringAggregateExpression aggregate)
        {
            // array_agg over no rows (or only filtered-out rows) is NULL.
            nullable = true;
            return aggregate.Update(
                Visit(aggregate.Value, out _),
                Visit(aggregate.Separator, out _),
                [.. aggregate.Orderings.Select(o => o.Update(Visit(o.Expression, out _)))]);
        }

        return base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable);
    }
}
