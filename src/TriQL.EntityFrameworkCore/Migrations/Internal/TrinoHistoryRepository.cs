using Microsoft.EntityFrameworkCore.Migrations;

namespace TriQL.EntityFrameworkCore.Migrations.Internal;

/// <summary>Migrations history. Migrations are not supported by the Trino provider; every member throws.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoHistoryRepository : HistoryRepository
{
    internal const string NotSupportedMessage =
        "Migrations are not supported by the Trino EF Core provider. Manage schemas and tables with SQL or your "
        + "own deployment tooling; for an existing database, scaffold a model with 'dotnet ef dbcontext scaffold'.";

    /// <summary>Initializes a new instance.</summary>
    public TrinoHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    /// <inheritdoc />
    protected override string ExistsSql => throw NotSupported();

    /// <inheritdoc />
    public override bool Exists() => throw NotSupported();

    /// <inheritdoc />
    public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => throw NotSupported();

    /// <inheritdoc />
    protected override bool InterpretExistsResult(object? value) => throw NotSupported();

    /// <inheritdoc />
    public override string GetCreateIfNotExistsScript() => throw NotSupported();

    /// <inheritdoc />
    public override string GetBeginIfNotExistsScript(string migrationId) => throw NotSupported();

    /// <inheritdoc />
    public override string GetBeginIfExistsScript(string migrationId) => throw NotSupported();

    /// <inheritdoc />
    public override string GetEndIfScript() => throw NotSupported();

    /// <inheritdoc />
    public override IMigrationsDatabaseLock AcquireDatabaseLock() => throw NotSupported();

    /// <inheritdoc />
    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default) =>
        throw NotSupported();

    internal static NotSupportedException NotSupported() => new(NotSupportedMessage);
}
