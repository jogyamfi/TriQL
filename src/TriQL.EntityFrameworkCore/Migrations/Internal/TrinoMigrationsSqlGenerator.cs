using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TriQL.EntityFrameworkCore.Storage.Internal;

namespace TriQL.EntityFrameworkCore.Migrations.Internal;

/// <summary>
/// Generates the development DDL behind <c>EnsureCreated</c>, <c>EnsureDeleted</c> and
/// <c>GenerateCreateScript</c>: <c>CREATE SCHEMA IF NOT EXISTS</c>, <c>CREATE TABLE</c> and
/// <c>DROP TABLE IF EXISTS</c>, with <c>"catalog"."schema"."table"</c> names when the model assigns a
/// catalog. This is not migrations support: <c>Migrate()</c> and <c>dotnet ef migrations</c> still throw.
/// </summary>
/// <remarks>
/// <para>
/// Tables get their columns, <c>NOT NULL</c> and comments; keys, foreign keys, unique constraints and indexes
/// are left out, because Trino and its connectors (Iceberg included) have none. Table properties such as
/// Iceberg's <c>format</c> or <c>partitioning</c> are left to the connector's defaults; create tables with SQL
/// when you need them. Every command runs outside a transaction (Trino has none). Any other operation throws.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Generates commands for schema creation, table creation and table removal. Index creation is skipped
    /// (Trino has no indexes); any other operation throws <see cref="NotSupportedException"/>.
    /// </summary>
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var supported = new List<MigrationOperation>();
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case EnsureSchemaOperation or CreateTableOperation or DropTableOperation:
                    supported.Add(operation);
                    break;
                case CreateIndexOperation:
                    break;
                default:
                    throw new NotSupportedException(
                        $"The Trino EF Core provider generates DDL only for EnsureCreated, EnsureDeleted and "
                        + $"GenerateCreateScript (schemas and tables), not for '{operation.GetType().Name}'. "
                        + "Manage schemas and tables with SQL or your own deployment tooling.");
            }
        }

        return base.Generate(supported, model, options);
    }

    /// <summary>
    /// <c>CREATE SCHEMA IF NOT EXISTS</c>, once for each catalog the model's tables in that schema use (a
    /// schema belongs to a catalog in Trino).
    /// </summary>
    protected override void Generate(EnsureSchemaOperation operation, IModel? model, MigrationCommandListBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var catalogs = model?.GetRelationalModel().Tables
            .Where(t => t.Schema == operation.Name)
            .Select(t => ((ITableBase)t).GetCatalog())
            .Distinct()
            .ToList();

        foreach (var catalog in catalogs is { Count: > 0 } ? catalogs : [null])
        {
            builder
                .Append("CREATE SCHEMA IF NOT EXISTS ")
                .Append(Qualified(catalog, operation.Name))
                .Append(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }
    }

    /// <summary><c>CREATE TABLE name ("column" type [NOT NULL] [COMMENT '…'], …) [COMMENT '…']</c>.</summary>
    protected override void Generate(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        var helper = Dependencies.SqlGenerationHelper;
        builder.Append("CREATE TABLE ").Append(TableName(operation.Name, operation.Schema, model)).AppendLine(" (");
        using (builder.Indent())
        {
            for (var i = 0; i < operation.Columns.Count; i++)
            {
                var column = operation.Columns[i];
                builder
                    .Append(helper.DelimitIdentifier(column.Name))
                    .Append(" ")
                    .Append(column.ColumnType ?? GetColumnType(operation.Schema, operation.Name, column.Name, column, model)!);
                if (!column.IsNullable)
                {
                    builder.Append(" NOT NULL");
                }

                if (column.Comment is { } comment)
                {
                    builder.Append(" COMMENT ").Append(TrinoSqlGenerationHelper.GenerateStringLiteral(comment));
                }

                builder.AppendLine(i < operation.Columns.Count - 1 ? "," : string.Empty);
            }
        }

        builder.Append(")");
        if (operation.Comment is { } tableComment)
        {
            builder.AppendLine().Append("COMMENT ").Append(TrinoSqlGenerationHelper.GenerateStringLiteral(tableComment));
        }

        if (terminate)
        {
            builder.Append(helper.StatementTerminator).EndCommand(suppressTransaction: true);
        }
    }

    /// <summary><c>DROP TABLE IF EXISTS name</c>, so that dropping a table that is already gone succeeds.</summary>
    protected override void Generate(DropTableOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append("DROP TABLE IF EXISTS ").Append(TableName(operation.Name, operation.Schema, model));
        if (terminate)
        {
            builder.Append(Dependencies.SqlGenerationHelper.StatementTerminator).EndCommand(suppressTransaction: true);
        }
    }

    private string TableName(string name, string? schema, IModel? model) =>
        Qualified(((ITableBase?)model?.GetRelationalModel().FindTable(name, schema))?.GetCatalog(), schema, name);

    private string Qualified(params string?[] parts) =>
        string.Join(".", parts.Where(p => p is not null).Select(p => Dependencies.SqlGenerationHelper.DelimitIdentifier(p!)));
}
