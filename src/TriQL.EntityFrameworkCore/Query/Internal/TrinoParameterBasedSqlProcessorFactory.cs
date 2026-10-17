using Microsoft.EntityFrameworkCore.Query;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Creates <see cref="TrinoParameterBasedSqlProcessor"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoParameterBasedSqlProcessorFactory : IRelationalParameterBasedSqlProcessorFactory
{
    private readonly RelationalParameterBasedSqlProcessorDependencies _dependencies;

    /// <summary>Initializes a new instance.</summary>
    public TrinoParameterBasedSqlProcessorFactory(RelationalParameterBasedSqlProcessorDependencies dependencies) => _dependencies = dependencies;

    /// <inheritdoc />
    public virtual RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters) =>
        new TrinoParameterBasedSqlProcessor(_dependencies, parameters);
}
