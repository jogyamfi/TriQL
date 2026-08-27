using System.Data;
using System.Data.Common;
using TriQL.Client;
using TriQL.Client.Exceptions;
using ClientParameterCollection = TriQL.Client.TrinoParameterCollection;

namespace TriQL.Data.ADO.Internal;

/// <summary>
/// Builds the twelve <c>GetSchema</c> collections (FR-9.5.2). Restriction values are always bound
/// parameters or client-side filters — never concatenated into generated SQL text (FR-9.5.3, SEC-4).
/// </summary>
internal static class SchemaCollections
{
    /// <summary>The name of the collection describing every other collection (FR-9.5.1).</summary>
    public const string MetaDataCollectionsName = "MetaDataCollections";

    private static readonly IReadOnlyDictionary<string, int> RestrictionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["MetaDataCollections"] = 0,
        ["DataSourceInformation"] = 0,
        ["DataTypes"] = 0,
        ["ReservedWords"] = 0,
        ["Restrictions"] = 0,
        ["Catalogs"] = 1,
        ["Schemas"] = 2,
        ["Tables"] = 4,
        ["Views"] = 3,
        ["Columns"] = 4,
        ["Functions"] = 1,
        ["SessionProperties"] = 1,
    };

    private static readonly (string TrinoName, string ClrTypeName, DbType ProviderType)[] TypeCatalog =
    [
        ("boolean", "System.Boolean", DbType.Boolean),
        ("tinyint", "System.SByte", DbType.SByte),
        ("smallint", "System.Int16", DbType.Int16),
        ("integer", "System.Int32", DbType.Int32),
        ("bigint", "System.Int64", DbType.Int64),
        ("real", "System.Single", DbType.Single),
        ("double", "System.Double", DbType.Double),
        ("decimal", "System.Decimal", DbType.Decimal),
        ("varchar", "System.String", DbType.String),
        ("char", "System.String", DbType.String),
        ("varbinary", "System.Byte[]", DbType.Binary),
        ("json", "System.String", DbType.String),
        ("date", "System.DateOnly", DbType.Date),
        ("time", "System.TimeOnly", DbType.Time),
        ("timestamp", "System.DateTime", DbType.DateTime),
        ("timestamp with time zone", "System.DateTimeOffset", DbType.DateTimeOffset),
        ("interval year to month", "TriQL.Client.Types.TrinoIntervalYearToMonth", DbType.Object),
        ("interval day to second", "System.TimeSpan", DbType.Object),
        ("uuid", "System.Guid", DbType.Guid),
        ("ipaddress", "System.Net.IPAddress", DbType.Object),
        ("array", "System.Object[]", DbType.Object),
        ("map", "System.Collections.Generic.IReadOnlyDictionary`2[System.Object,System.Object]", DbType.Object),
        ("row", "TriQL.Client.Types.ITrinoRowValue", DbType.Object),
    ];

    private static readonly string[] ReservedWordList =
    [
        "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "HAVING", "AS", "AND", "OR", "NOT", "JOIN", "LEFT",
        "RIGHT", "FULL", "INNER", "OUTER", "ON", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "TABLE", "DROP", "ALTER", "WITH", "UNION", "ALL", "DISTINCT", "LIMIT", "OFFSET", "CASE",
        "WHEN", "THEN", "ELSE", "END", "NULL", "TRUE", "FALSE", "IN", "EXISTS", "BETWEEN", "LIKE", "IS",
        "CAST", "USING", "EXECUTE", "PREPARE", "DEALLOCATE",
    ];

    /// <summary>Builds the requested collection (FR-9.5.2, FR-9.5.4, FR-9.5.5).</summary>
    public static async Task<DataTable> GetSchemaAsync(
        TrinoClient? client, string collectionName, string?[]? restrictions, CancellationToken cancellationToken)
    {
        if (!RestrictionCounts.TryGetValue(collectionName, out var maxRestrictions))
        {
            throw new ArgumentException(
                $"Unsupported schema collection '{collectionName}'. Supported: {string.Join(", ", RestrictionCounts.Keys)}.",
                nameof(collectionName));
        }

        if (restrictions is { Length: > 0 } && restrictions.Length > maxRestrictions)
        {
            throw new ArgumentException(
                $"Collection '{collectionName}' supports at most {maxRestrictions} restriction value(s), but {restrictions.Length} were supplied.",
                nameof(restrictions));
        }

        // CA2000: ownership of the returned DataTable transfers to the caller (the standard
        // DbConnection.GetSchema() contract), so there is nothing to dispose here.
#pragma warning disable CA2000
        return collectionName switch
        {
            "MetaDataCollections" => BuildMetaDataCollections(),
            "DataTypes" => BuildDataTypes(),
            "ReservedWords" => BuildReservedWords(),
            "Restrictions" => BuildRestrictions(),
            "DataSourceInformation" => await BuildDataSourceInformationAsync(RequireClient(client), cancellationToken).ConfigureAwait(false),
            "Catalogs" => await BuildFilteredShowAsync(RequireClient(client), "SHOW CATALOGS", "Catalogs", Restriction(restrictions, 0), cancellationToken).ConfigureAwait(false),
            "Functions" => await BuildFilteredShowAsync(RequireClient(client), "SHOW FUNCTIONS", "Functions", Restriction(restrictions, 0), cancellationToken).ConfigureAwait(false),
            "SessionProperties" => await BuildFilteredShowAsync(RequireClient(client), "SHOW SESSION", "SessionProperties", Restriction(restrictions, 0), cancellationToken).ConfigureAwait(false),
            "Schemas" => await BuildInformationSchemaAsync(RequireClient(client), "schemata", ["schema_name"], restrictions, cancellationToken).ConfigureAwait(false),
            "Tables" => await BuildInformationSchemaAsync(RequireClient(client), "tables", ["table_schema", "table_name", "table_type"], restrictions, cancellationToken).ConfigureAwait(false),
            "Views" => await BuildInformationSchemaAsync(RequireClient(client), "views", ["table_schema", "table_name"], restrictions, cancellationToken).ConfigureAwait(false),
            "Columns" => await BuildInformationSchemaAsync(RequireClient(client), "columns", ["table_schema", "table_name", "column_name"], restrictions, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unsupported schema collection '{collectionName}'.", nameof(collectionName)),
        };
#pragma warning restore CA2000
    }


    private static DataTable BuildMetaDataCollections()
    {
        var table = new DataTable(MetaDataCollectionsName);
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("NumberOfRestrictions", typeof(int));
        table.Columns.Add("NumberOfIdentifierParts", typeof(int));

        foreach (var (name, restrictionCount) in RestrictionCounts)
        {
            table.Rows.Add(name, restrictionCount, IdentifierParts(name));
        }

        return table;
    }

    private static int IdentifierParts(string collectionName) => collectionName switch
    {
        "Tables" or "Views" or "Columns" => 3,
        "Schemas" => 2,
        _ => 0,
    };

    private static DataTable BuildDataTypes()
    {
        var table = new DataTable("DataTypes");
        table.Columns.Add("TypeName", typeof(string));
        table.Columns.Add("ProviderDbType", typeof(int));
        table.Columns.Add("DataType", typeof(string));
        table.Columns.Add("IsNullable", typeof(bool));
        table.Columns.Add("IsSearchable", typeof(bool));

        foreach (var (name, clrType, dbType) in TypeCatalog)
        {
            table.Rows.Add(name, (int)dbType, clrType, true, true);
        }

        return table;
    }

    private static DataTable BuildReservedWords()
    {
        var table = new DataTable("ReservedWords");
        table.Columns.Add("ReservedWord", typeof(string));
        foreach (var word in ReservedWordList)
        {
            table.Rows.Add(word);
        }

        return table;
    }

    private static DataTable BuildRestrictions()
    {
        var table = new DataTable("Restrictions");
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("RestrictionName", typeof(string));
        table.Columns.Add("RestrictionDefault", typeof(string));
        table.Columns.Add("RestrictionNumber", typeof(int));

        AddRestrictionRows(table, "Catalogs", "catalog");
        AddRestrictionRows(table, "Schemas", "catalog", "schema");
        AddRestrictionRows(table, "Tables", "catalog", "schema", "table", "table_type");
        AddRestrictionRows(table, "Views", "catalog", "schema", "table");
        AddRestrictionRows(table, "Columns", "catalog", "schema", "table", "column");
        AddRestrictionRows(table, "Functions", "function");
        AddRestrictionRows(table, "SessionProperties", "name");

        return table;
    }

    private static void AddRestrictionRows(DataTable table, string collection, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            table.Rows.Add(collection, names[i], names[i], i + 1);
        }
    }

    private static async Task<DataTable> BuildDataSourceInformationAsync(TrinoClient client, CancellationToken cancellationToken)
    {
        var info = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);

        var table = new DataTable("DataSourceInformation");
        table.Columns.Add("CompositeIdentifierSeparatorPattern", typeof(string));
        table.Columns.Add("DataSourceProductName", typeof(string));
        table.Columns.Add("DataSourceProductVersion", typeof(string));
        table.Columns.Add("DataSourceProductVersionNormalized", typeof(string));
        table.Columns.Add("GroupByBehavior", typeof(int));
        table.Columns.Add("IdentifierPattern", typeof(string));
        table.Columns.Add("IdentifierCase", typeof(int));
        table.Columns.Add("OrderByColumnsInSelect", typeof(bool));
        table.Columns.Add("ParameterMarkerFormat", typeof(string));
        table.Columns.Add("ParameterMarkerPattern", typeof(string));
        table.Columns.Add("ParameterNameMaxLength", typeof(int));
        table.Columns.Add("ParameterNamePattern", typeof(string));
        table.Columns.Add("QuotedIdentifierPattern", typeof(string));
        table.Columns.Add("QuotedIdentifierCase", typeof(int));
        table.Columns.Add("StatementSeparatorPattern", typeof(string));
        table.Columns.Add("StringLiteralPattern", typeof(string));
        table.Columns.Add("SupportedJoinOperators", typeof(int));

        var row = table.NewRow();
        row["CompositeIdentifierSeparatorPattern"] = @"\.";
        row["DataSourceProductName"] = "Trino";
        row["DataSourceProductVersion"] = info.Version;
        row["DataSourceProductVersionNormalized"] = info.Version;
        row["GroupByBehavior"] = (int)GroupByBehavior.Unrelated;
        row["IdentifierPattern"] = "[A-Za-z_][A-Za-z0-9_]*";
        row["IdentifierCase"] = (int)IdentifierCase.Insensitive;
        row["OrderByColumnsInSelect"] = false;
        row["ParameterMarkerFormat"] = "?";
        row["ParameterMarkerPattern"] = @"\?";
        row["ParameterNameMaxLength"] = 0;
        row["ParameterNamePattern"] = string.Empty;
        row["QuotedIdentifierPattern"] = "\"(([^\"]|\"\")*)\"";
        row["QuotedIdentifierCase"] = (int)IdentifierCase.Sensitive;
        row["StatementSeparatorPattern"] = ";";
        row["StringLiteralPattern"] = "'(([^']|'')*)'";
        row["SupportedJoinOperators"] =
            (int)(SupportedJoinOperators.Inner | SupportedJoinOperators.LeftOuter | SupportedJoinOperators.RightOuter | SupportedJoinOperators.FullOuter);
        table.Rows.Add(row);

        return table;
    }

    private static async Task<DataTable> BuildFilteredShowAsync(
        TrinoClient client, string sql, string tableName, string? restriction, CancellationToken cancellationToken)
    {
        var result = new DataTable(tableName);
        var columnsInitialized = false;

        var resultSet = await client.ExecuteAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (resultSet.ConfigureAwait(false))
        {
            await foreach (var row in resultSet.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (restriction is not null
                    && (row.FieldCount == 0 || !string.Equals(row.GetValue(0) as string, restriction, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!columnsInitialized)
                {
                    InitializeColumns(result, resultSet.Columns);
                    columnsInitialized = true;
                }

                AppendRow(result, row);
            }
        }

        return result;
    }

    /// <summary>
    /// Queries an <c>information_schema</c> table, scoped to a single catalog when the catalog
    /// restriction (always restriction index 0) is supplied, or across every catalog otherwise.
    /// Every non-catalog restriction is bound as a <c>?</c> parameter, never concatenated into the
    /// generated SQL text (FR-9.5.3, FR-9.5.7, SEC-4).
    /// </summary>
    private static async Task<DataTable> BuildInformationSchemaAsync(
        TrinoClient client, string infoSchemaTable, string[] otherRestrictionColumns, string?[]? restrictions, CancellationToken cancellationToken)
    {
        var catalogRestriction = Restriction(restrictions, 0);
        var catalogs = catalogRestriction is not null
            ? (IReadOnlyList<string>)[catalogRestriction]
            : await ListCatalogsAsync(client, cancellationToken).ConfigureAwait(false);

        var result = new DataTable(infoSchemaTable);
        var columnsInitialized = false;

        foreach (var catalog in catalogs)
        {
            var whereClauses = new List<string>();
            ClientParameterCollection? parameters = null;
            for (var i = 0; i < otherRestrictionColumns.Length; i++)
            {
                var value = Restriction(restrictions, i + 1);
                if (value is null)
                {
                    continue;
                }

                parameters ??= new ClientParameterCollection();
                whereClauses.Add($"{otherRestrictionColumns[i]} = ?");
                parameters.Add(value);
            }

            var sql = $"SELECT * FROM {IdentifierQuoting.Quote(catalog)}.information_schema.{infoSchemaTable}"
                + (whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : string.Empty);

            TrinoResultSet resultSet;
            try
            {
                resultSet = await client.ExecuteAsync(sql, parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (TrinoException)
            {
                // Best-effort across catalogs: some connectors don't expose this information_schema table.
                continue;
            }

            await using (resultSet.ConfigureAwait(false))
            {
                await foreach (var row in resultSet.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!columnsInitialized)
                    {
                        InitializeColumns(result, resultSet.Columns);
                        columnsInitialized = true;
                    }

                    AppendRow(result, row);
                }
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> ListCatalogsAsync(TrinoClient client, CancellationToken cancellationToken)
    {
        var catalogs = new List<string>();
        var resultSet = await client.ExecuteAsync("SHOW CATALOGS", cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (resultSet.ConfigureAwait(false))
        {
            await foreach (var row in resultSet.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (row.FieldCount > 0 && row.GetValue(0) is string name)
                {
                    catalogs.Add(name);
                }
            }
        }

        return catalogs;
    }

    private static void InitializeColumns(DataTable table, IReadOnlyList<TrinoColumn> columns)
    {
        foreach (var column in columns)
        {
            table.Columns.Add(new DataColumn(column.Name, typeof(object)));
        }
    }

    private static void AppendRow(DataTable table, TrinoRow row)
    {
        var values = new object?[row.FieldCount];
        for (var i = 0; i < row.FieldCount; i++)
        {
            values[i] = row.IsDBNull(i) ? DBNull.Value : row.GetValue(i);
        }

        table.Rows.Add(values);
    }

    private static string? Restriction(string?[]? restrictions, int index) =>
        restrictions is not null && index < restrictions.Length ? restrictions[index] : null;

    private static TrinoClient RequireClient(TrinoClient? client) =>
        client ?? throw new InvalidOperationException("The connection must be open to query this schema collection.");
}
