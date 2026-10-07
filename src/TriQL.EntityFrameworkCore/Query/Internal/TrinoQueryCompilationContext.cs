using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace TriQL.EntityFrameworkCore.Query.Internal;

/// <summary>
/// The query compilation context for the Trino provider. Split queries are always buffered: a
/// <see cref="Data.ADO.TrinoConnection"/> runs one command at a time, so EF must read each split
/// query to the end before starting the next, instead of keeping the first reader open (which
/// needs multiple active result sets). Single queries still stream.
/// </summary>
/// <remarks>
/// The same approach as SQL Server's provider takes when MARS is off.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoQueryCompilationContext : RelationalQueryCompilationContext
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoQueryCompilationContext(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        bool async)
        : base(dependencies, relationalDependencies, async)
    {
    }

    /// <inheritdoc />
    public override bool IsBuffering =>
        base.IsBuffering || QuerySplittingBehavior == Microsoft.EntityFrameworkCore.QuerySplittingBehavior.SplitQuery;
}

/// <summary>Creates <see cref="TrinoQueryCompilationContext"/> instances.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoQueryCompilationContextFactory : IQueryCompilationContextFactory
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoQueryCompilationContextFactory(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies)
    {
        Dependencies = dependencies;
        RelationalDependencies = relationalDependencies;
    }

    /// <summary>Dependencies for this service.</summary>
    protected virtual QueryCompilationContextDependencies Dependencies { get; }

    /// <summary>Relational provider-specific dependencies for this service.</summary>
    protected virtual RelationalQueryCompilationContextDependencies RelationalDependencies { get; }

    /// <inheritdoc />
    public virtual QueryCompilationContext Create(bool async) =>
        new TrinoQueryCompilationContext(Dependencies, RelationalDependencies, async);

    /// <summary>
    /// Always throws: precompiled queries exist for NativeAOT, which EF Core's runtime and this
    /// provider do not support (the package is neither trimmable nor AOT-compatible).
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual QueryCompilationContext CreatePrecompiled(bool async) =>
        throw new NotSupportedException("Precompiled queries (NativeAOT) are not supported by the Trino EF Core provider.");
}
