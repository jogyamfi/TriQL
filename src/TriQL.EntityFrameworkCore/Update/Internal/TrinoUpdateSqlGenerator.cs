using Microsoft.EntityFrameworkCore.Update;

namespace TriQL.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Generates <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c> statements for <c>SaveChanges</c>. EF's
/// defaults for now; Phase 6 adds Trino's rows-affected handling and concurrency checks.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoUpdateSqlGenerator : UpdateSqlGenerator
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }
}
