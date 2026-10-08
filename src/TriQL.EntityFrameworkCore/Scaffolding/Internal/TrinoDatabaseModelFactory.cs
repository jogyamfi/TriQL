using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Diagnostics.Internal;
using TriQL.EntityFrameworkCore.Metadata.Internal;

namespace TriQL.EntityFrameworkCore.Scaffolding.Internal;

/// <summary>
/// Reads the tables, views and columns of one Trino catalog for <c>dotnet ef dbcontext scaffold</c>.
/// </summary>
/// <remarks>
/// <para>
/// Columns and their comments come from <c>system.jdbc.columns</c> and table comments from
/// <c>system.metadata.table_comments</c>: Trino's <c>information_schema.columns</c> has no comment column
/// (measured on 466). Every filter value is a bound parameter.
/// </para>
/// <para>
/// The catalog is the connection's, or the one named by <c>--schema catalog.schema</c> (one catalog per run).
/// Scaffolding another catalog than the connection's adds <c>HasDefaultCatalog</c> to the model. Without
/// <c>--schema</c>, every schema except the connector metadata schemas <c>information_schema</c> and
/// <c>system</c> is read. <c>--table</c> accepts
/// <c>table</c>, <c>schema.table</c> or <c>catalog.schema.table</c>.
/// </para>
/// <para>
/// Trino exposes no primary or foreign keys (Iceberg has none), so every table scaffolds keyless
/// (<c>HasNoKey()</c>, with EF's warning); configure <c>HasKey</c> for the tables you write to. Columns of
/// types with no .NET mapping (<c>array</c>, <c>map</c>, <c>row</c>, <c>json</c>, <c>ipaddress</c>,
/// intervals, …) are skipped with a <see cref="TrinoEventId.ColumnSkipped"/> warning.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoDatabaseModelFactory : DatabaseModelFactory
{
    // Connector metadata, not user tables: Trino's information_schema in every catalog, and the "system"
    // schema some connectors add (Iceberg on Trino 483 has system.iceberg_tables). Scaffolded only when
    // named with --schema.
    private const string MetadataSchemas = "'information_schema', 'system'";

    private readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger, IRelationalTypeMappingSource typeMappingSource)
    {
        _logger = logger;
        _typeMappingSource = typeMappingSource;
    }

    /// <inheritdoc />
    public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        using var connection = new TrinoConnection(connectionString);
        return Create(connection, options);
    }

    /// <inheritdoc />
    public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = new TrinoConnectionStringBuilder(connection.ConnectionString);
        var (catalog, schemas) = ResolveCatalogAndSchemas(connectionString.Catalog, options.Schemas);
        var tableFilter = options.Tables.Select(ParseTableName).ToList();

        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            connection.Open();
        }

        try
        {
            var databaseModel = new DatabaseModel { DatabaseName = catalog };
            if (string.Equals(catalog, connectionString.Catalog, StringComparison.OrdinalIgnoreCase))
            {
                databaseModel.DefaultSchema = connectionString.Schema;
            }
            else
            {
                databaseModel[TrinoAnnotationNames.DefaultCatalog] = catalog;
            }

            var comments = ReadTableComments(connection, catalog, schemas);
            foreach (var (schema, name, type) in ReadTables(connection, catalog, schemas))
            {
                if (tableFilter.Count > 0 && !tableFilter.Any(f => f.Matches(catalog, schema, name)))
                {
                    continue;
                }

                DatabaseTable table = type == "VIEW" ? new DatabaseView() : new DatabaseTable();
                table.Database = databaseModel;
                table.Schema = schema;
                table.Name = name;
                table.Comment = comments.GetValueOrDefault((schema, name));
                databaseModel.Tables.Add(table);
            }

            AddColumns(connection, catalog, schemas, databaseModel);
            return databaseModel;
        }
        finally
        {
            if (!wasOpen)
            {
                connection.Close();
            }
        }
    }

    private void AddColumns(DbConnection connection, string catalog, IReadOnlyList<string> schemas, DatabaseModel databaseModel)
    {
        var tables = databaseModel.Tables.ToDictionary(t => (t.Schema!, t.Name));
        using var command = CreateCommand(
            connection,
            "SELECT table_schem, table_name, column_name, type_name, is_nullable, remarks FROM system.jdbc.columns WHERE table_cat = @catalog",
            catalog,
            schemas,
            schemaColumn: "table_schem",
            orderBy: "table_schem, table_name, ordinal_position");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!tables.TryGetValue((reader.GetString(0), reader.GetString(1)), out var table))
            {
                continue;
            }

            var columnName = reader.GetString(2);
            var storeType = reader.GetString(3);
            if (_typeMappingSource.FindMapping(storeType) is null)
            {
                _logger.ColumnSkipped($"{table.Schema}.{table.Name}", columnName, storeType);
                continue;
            }

            table.Columns.Add(new DatabaseColumn
            {
                Table = table,
                Name = columnName,
                StoreType = storeType,
                IsNullable = !string.Equals(reader.GetString(4), "NO", StringComparison.OrdinalIgnoreCase),
                Comment = reader.IsDBNull(5) || reader.GetString(5).Length == 0 ? null : reader.GetString(5),
            });
        }
    }

    private static List<(string Schema, string Name, string Type)> ReadTables(DbConnection connection, string catalog, IReadOnlyList<string> schemas)
    {
        using var command = CreateCommand(
            connection,
            "SELECT table_schem, table_name, table_type FROM system.jdbc.tables WHERE table_cat = @catalog AND table_type IN ('TABLE', 'VIEW')",
            catalog,
            schemas,
            schemaColumn: "table_schem",
            orderBy: "table_schem, table_name");
        using var reader = command.ExecuteReader();
        var tables = new List<(string, string, string)>();
        while (reader.Read())
        {
            tables.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return tables;
    }

    private static Dictionary<(string Schema, string Name), string> ReadTableComments(DbConnection connection, string catalog, IReadOnlyList<string> schemas)
    {
        using var command = CreateCommand(
            connection,
            "SELECT schema_name, table_name, comment FROM system.metadata.table_comments WHERE catalog_name = @catalog AND comment IS NOT NULL AND comment <> ''",
            catalog,
            schemas,
            schemaColumn: "schema_name",
            orderBy: null);
        using var reader = command.ExecuteReader();
        var comments = new Dictionary<(string, string), string>();
        while (reader.Read())
        {
            comments[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return comments;
    }

    /// <summary>
    /// A query filtered by catalog and, when given, schemas (all bound parameters); otherwise every schema
    /// but the metadata schemas (<c>information_schema</c>, <c>system</c>).
    /// </summary>
    private static DbCommand CreateCommand(DbConnection connection, string sql, string catalog, IReadOnlyList<string> schemas, string schemaColumn, string? orderBy)
    {
        var command = connection.CreateCommand();
        AddParameter(command, "catalog", catalog);
        if (schemas.Count == 0)
        {
            sql += $" AND {schemaColumn} NOT IN ({MetadataSchemas})";
        }
        else
        {
            var names = new List<string>();
            for (var i = 0; i < schemas.Count; i++)
            {
                names.Add("@schema" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddParameter(command, "schema" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), schemas[i]);
            }

            sql += $" AND {schemaColumn} IN ({string.Join(", ", names)})";
        }

#pragma warning disable CA2100 // The SQL is fixed text plus generated parameter names; every value is bound.
        command.CommandText = orderBy is null ? sql : sql + " ORDER BY " + orderBy;
#pragma warning restore CA2100
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// The catalog to read (the connection's, or the one in <c>catalog.schema</c> options) and the schemas,
    /// lower-cased as Trino stores them.
    /// </summary>
    private static (string Catalog, IReadOnlyList<string> Schemas) ResolveCatalogAndSchemas(string? connectionCatalog, IEnumerable<string> schemaOptions)
    {
        var catalogs = new HashSet<string>(StringComparer.Ordinal);
        var schemas = new List<string>();
        foreach (var option in schemaOptions)
        {
            var parts = option.Split('.');
            if (parts.Length == 2)
            {
                catalogs.Add(parts[0].ToLowerInvariant());
                schemas.Add(parts[1].ToLowerInvariant());
            }
            else
            {
                catalogs.Add(connectionCatalog?.ToLowerInvariant() ?? string.Empty);
                schemas.Add(option.ToLowerInvariant());
            }
        }

        if (catalogs.Count > 1)
        {
            throw new InvalidOperationException(
                $"The --schema options name more than one catalog ({string.Join(", ", catalogs.Order(StringComparer.Ordinal))}). "
                + "The Trino provider scaffolds one catalog at a time: run the command once per catalog.");
        }

        var catalog = catalogs.SingleOrDefault() ?? connectionCatalog?.ToLowerInvariant();
        if (string.IsNullOrEmpty(catalog))
        {
            throw new InvalidOperationException(
                "Scaffolding needs a catalog: set Catalog in the connection string, or pass --schema catalog.schema.");
        }

        return (catalog, schemas);
    }

    private static TableName ParseTableName(string table)
    {
        var parts = table.ToLowerInvariant().Split('.');
        return parts.Length switch
        {
            3 => new TableName(parts[0], parts[1], parts[2]),
            2 => new TableName(null, parts[0], parts[1]),
            _ => new TableName(null, null, parts[0]),
        };
    }

    private sealed record TableName(string? Catalog, string? Schema, string Name)
    {
        public bool Matches(string catalog, string schema, string name) =>
            (Catalog is null || Catalog == catalog) && (Schema is null || Schema == schema) && Name == name;
    }
}
