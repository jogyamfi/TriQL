using Microsoft.EntityFrameworkCore.Migrations;

namespace TriQL.EntityFrameworkCore.Migrations.Internal;

/// <summary>
/// Replaces EF's migrator so that <c>Migrate</c> and script generation report that migrations are
/// unsupported, rather than failing on an earlier step such as probing for the history table.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoMigrator : IMigrator
{
    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void Migrate(string? targetMigration) => throw TrinoHistoryRepository.NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual Task MigrateAsync(string? targetMigration, CancellationToken cancellationToken = default) =>
        throw TrinoHistoryRepository.NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual string GenerateScript(
        string? fromMigration = null,
        string? toMigration = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) =>
        throw TrinoHistoryRepository.NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual bool HasPendingModelChanges() => throw TrinoHistoryRepository.NotSupported();
}
