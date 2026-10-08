using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Processes the SQL tree once parameter values are known, using <see cref="TrinoSqlNullabilityProcessor"/>.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoParameterBasedSqlProcessor : RelationalParameterBasedSqlProcessor
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoParameterBasedSqlProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    /// <inheritdoc />
    protected override Expression ProcessSqlNullability(Expression queryExpression, ParametersCacheDecorator Decorator) =>
        new TrinoSqlNullabilityProcessor(Dependencies, Parameters).Process(queryExpression, Decorator);
}
