using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace TriQL.EntityFrameworkCore.Migrations.Internal;

/// <summary>
/// Generates DDL for migration operations. Until EF8-T1 adds the development-only subset used by
/// <c>EnsureCreated</c> (schemas and tables), every operation throws.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) =>
        throw TrinoHistoryRepository.NotSupported();
}
