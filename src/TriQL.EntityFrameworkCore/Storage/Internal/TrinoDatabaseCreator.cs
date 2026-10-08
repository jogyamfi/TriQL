using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// <c>EnsureCreated</c>, <c>EnsureDeleted</c>, <c>GenerateCreateScript</c> and <c>CanConnect</c> for Trino,
/// for development and tests. A Trino catalog exists independently of EF, so the "database" is the model's
/// tables: <c>EnsureCreated</c> creates the model's schemas (<c>IF NOT EXISTS</c>) and tables when none of
/// them exists, and <c>EnsureDeleted</c> drops <b>only the model's tables</b>, never schemas or catalogs,
/// which may hold other data.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoDatabaseCreator : RelationalDatabaseCreator
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Returns <see langword="true"/> when Trino can be reached and runs <c>SELECT 1</c>. A Trino catalog
    /// exists independently of EF, so reachability is the only meaningful check.
    /// </summary>
    public override bool Exists() => ExecuteScalar("SELECT 1") is not null;

    /// <inheritdoc cref="Exists" />
    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        await ExecuteScalarAsync("SELECT 1", cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Whether any of the model's tables exists, according to <c>information_schema.tables</c>.</summary>
    public override bool HasTables() =>
        TablesExistSql().Any(sql => Convert.ToInt64(ExecuteScalar(sql), System.Globalization.CultureInfo.InvariantCulture) > 0);

    /// <inheritdoc cref="HasTables" />
    public override async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var sql in TablesExistSql())
        {
            if (Convert.ToInt64(await ExecuteScalarAsync(sql, cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Does nothing: catalogs exist independently of EF; schemas are created with the tables.</summary>
    public override void Create()
    {
    }

    /// <inheritdoc cref="Create" />
    public override Task CreateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Drops the model's tables (<c>DROP TABLE IF EXISTS</c>); schemas and catalogs are kept.</summary>
    public override void Delete() =>
        Dependencies.MigrationCommandExecutor.ExecuteNonQuery(GetDropTablesCommands(), Dependencies.Connection);

    /// <inheritdoc cref="Delete" />
    public override Task DeleteAsync(CancellationToken cancellationToken = default) =>
        Dependencies.MigrationCommandExecutor.ExecuteNonQueryAsync(GetDropTablesCommands(), Dependencies.Connection, cancellationToken);

    /// <summary>Drops the model's tables when any of them exists; returns whether it did.</summary>
    public override bool EnsureDeleted()
    {
        if (!HasTables())
        {
            return false;
        }

        Delete();
        return true;
    }

    /// <inheritdoc cref="EnsureDeleted" />
    public override async Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    {
        if (!await HasTablesAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await DeleteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.MigrationCommand> GetDropTablesCommands()
    {
        var model = Dependencies.CurrentContext.Context.GetService<IDesignTimeModel>().Model;
        var operations = model.GetRelationalModel().Tables
            .Select(t => (MigrationOperation)new DropTableOperation { Name = t.Name, Schema = t.Schema })
            .ToList();
        return Dependencies.MigrationsSqlGenerator.Generate(operations, model);
    }

    /// <summary>
    /// One <c>count(*)</c> query per catalog over its <c>information_schema.tables</c>, matching the model's
    /// tables. Trino stores identifiers in lower case, so names are compared in lower case; a table without a
    /// schema is looked up in the connection's schema (<c>current_schema</c>).
    /// </summary>
    private List<string> TablesExistSql()
    {
        var helper = Dependencies.SqlGenerationHelper;
        var queries = new List<string>();
        foreach (var catalog in Dependencies.CurrentContext.Context.Model.GetRelationalModel().Tables.GroupBy(t => ((ITableBase)t).GetCatalog()))
        {
            var sql = new StringBuilder("SELECT count(*) FROM ");
            if (catalog.Key is not null)
            {
                sql.Append(helper.DelimitIdentifier(catalog.Key)).Append('.');
            }

            sql.Append("information_schema.tables WHERE ");
            var first = true;
            foreach (var table in catalog)
            {
                sql.Append(first ? "(" : " OR (");
                sql.Append("table_schema = ")
                    .Append(table.Schema is null ? "current_schema" : TrinoSqlGenerationHelper.GenerateStringLiteral(table.Schema.ToLowerInvariant()))
                    .Append(" AND table_name = ")
                    .Append(TrinoSqlGenerationHelper.GenerateStringLiteral(table.Name.ToLowerInvariant()))
                    .Append(')');
                first = false;
            }

            queries.Add(sql.ToString());
        }

        return queries;
    }

    private object? ExecuteScalar(string sql)
    {
        var connection = Dependencies.Connection;
        connection.Open();
        try
        {
            return RawSqlCommandBuilder.Build(sql).ExecuteScalar(CreateParameterObject(connection));
        }
        finally
        {
            connection.Close();
        }
    }

    private async Task<object?> ExecuteScalarAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = Dependencies.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RawSqlCommandBuilder.Build(sql)
                .ExecuteScalarAsync(CreateParameterObject(connection), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private IRawSqlCommandBuilder RawSqlCommandBuilder => Dependencies.CurrentContext.Context.GetService<IRawSqlCommandBuilder>();

    private RelationalCommandParameterObject CreateParameterObject(IRelationalConnection connection) =>
        new(connection, parameterValues: null, readerColumns: null, Dependencies.CurrentContext.Context, Dependencies.CommandLogger, CommandSource.Migrations);
}
