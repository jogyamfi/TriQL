using Microsoft.EntityFrameworkCore.Query;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>Creates <see cref="TrinoQuerySqlGenerator"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    /// <summary>Initializes a new instance.</summary>
    public TrinoQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies) => _dependencies = dependencies;

    /// <inheritdoc />
    public virtual QuerySqlGenerator Create() => new TrinoQuerySqlGenerator(_dependencies);
}
