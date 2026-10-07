using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Supports <c>CanConnect</c> (<c>SELECT 1</c>). <c>EnsureCreated</c>, <c>EnsureDeleted</c> and
/// <c>GenerateCreateScript</c> are not implemented yet (EF8-T1); until then they throw
/// <see cref="NotSupportedException"/> rather than running DDL that has not been verified.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoDatabaseCreator : RelationalDatabaseCreator
{
    internal const string NotYetSupportedMessage =
        "EnsureCreated, EnsureDeleted and GenerateCreateScript are not supported yet by the Trino EF Core provider. "
        + "Create schemas and tables with SQL, for example through Database.ExecuteSql.";

    /// <summary>Initializes a new instance.</summary>
    public TrinoDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Returns <see langword="true"/> when Trino can be reached and runs <c>SELECT 1</c>. A Trino
    /// "database" (catalog) exists independently of EF, so reachability is the only meaningful check.
    /// </summary>
    public override bool Exists()
    {
        var connection = Dependencies.Connection;
        connection.Open();
        try
        {
            RawSqlCommandBuilder.Build("SELECT 1").ExecuteScalar(CreateParameterObject(connection));
            return true;
        }
        finally
        {
            connection.Close();
        }
    }

    /// <inheritdoc cref="Exists" />
    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        var connection = Dependencies.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RawSqlCommandBuilder.Build("SELECT 1")
                .ExecuteScalarAsync(CreateParameterObject(connection), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Always throws until EF8-T1.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override bool HasTables() => throw NotYetSupported();

    /// <summary>Always throws until EF8-T1.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Create() => throw NotYetSupported();

    /// <summary>Always throws until EF8-T1.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Delete() => throw NotYetSupported();

    /// <summary>Always throws until EF8-T1.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override string GenerateCreateScript() => throw NotYetSupported();

    private IRawSqlCommandBuilder RawSqlCommandBuilder => Dependencies.CurrentContext.Context.GetService<IRawSqlCommandBuilder>();

    private RelationalCommandParameterObject CreateParameterObject(IRelationalConnection connection) =>
        new(connection, parameterValues: null, readerColumns: null, Dependencies.CurrentContext.Context, Dependencies.CommandLogger, CommandSource.Migrations);

    private static NotSupportedException NotYetSupported() => new(NotYetSupportedMessage);
}
