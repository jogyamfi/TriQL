using System.Data;
using TriQL.Data.ADO;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P4-T24: all twelve <c>GetSchema</c> collections (FR-9.5.2) against a live container. Each
/// collection is asserted to return a non-null <see cref="DataTable"/>, with row-content
/// assertions wherever the live server is guaranteed to have data for that collection.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class AdoSchemaCollectionTests : IAsyncLifetime, IDisposable
{
    private static readonly string[] ExpectedCollectionNames =
    [
        "MetaDataCollections", "DataSourceInformation", "DataTypes", "ReservedWords", "Restrictions",
        "Catalogs", "Schemas", "Tables", "Views", "Columns", "Functions", "SessionProperties",
    ];

    private readonly TrinoContainerFixture _fixture;
    private TrinoConnection _connection = null!;

    public AdoSchemaCollectionTests(TrinoContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connection = new TrinoConnection(new TrinoConnectionStringBuilder { Server = _fixture.ServerUri.ToString() }.ToString());
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: the analyzer requires an IDisposable implementation on any type owning a
    // disposable field; xUnit actually drives cleanup through IAsyncLifetime.DisposeAsync above.
    public void Dispose() => _connection.Dispose();

    [Fact]
    public void GetSchema_NoArguments_ListsAllTwelveCollections()
    {
        var table = _connection.GetSchema();

        Assert.Equal("MetaDataCollections", table.TableName);
        Assert.Equal(12, table.Rows.Count);

        var names = table.Rows.Cast<DataRow>().Select(r => (string)r["CollectionName"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in ExpectedCollectionNames)
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void GetSchema_CollectionName_Overload_WorksThroughTheSyncBridge()
    {
        // Exercises the sync GetSchema(string) overload (routed through SyncBridge, FR-9.5.6)
        // against a live server, not just the parameterless one covered above.
        var table = _connection.GetSchema("Catalogs");

        Assert.NotNull(table);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r[0] == "tpch");
    }

    [Fact]
    public async Task GetSchemaAsync_DataSourceInformation_ReflectsTheLiveServer()
    {
        var table = await _connection.GetSchemaAsync("DataSourceInformation");

        Assert.Single(table.Rows);
        var row = table.Rows[0];
        Assert.Equal("Trino", row["DataSourceProductName"]);
        Assert.False(string.IsNullOrEmpty((string)row["DataSourceProductVersion"]));
    }

    [Fact]
    public async Task GetSchemaAsync_DataTypes_IncludesTheFR72Catalog()
    {
        var table = await _connection.GetSchemaAsync("DataTypes");

        Assert.True(table.Rows.Count > 0);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["TypeName"] == "bigint");
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["TypeName"] == "row");
    }

    [Fact]
    public async Task GetSchemaAsync_ReservedWords_ReturnsWords()
    {
        var table = await _connection.GetSchemaAsync("ReservedWords");
        Assert.True(table.Rows.Count > 0);
    }

    [Fact]
    public async Task GetSchemaAsync_Restrictions_ListsRestrictionsForEveryCollection()
    {
        var table = await _connection.GetSchemaAsync("Restrictions");

        Assert.True(table.Rows.Count > 0);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["CollectionName"] == "Tables" && (string)r["RestrictionName"] == "catalog");
    }

    [Fact]
    public async Task GetSchemaAsync_Catalogs_ReturnsTpch()
    {
        var table = await _connection.GetSchemaAsync("Catalogs");
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r[0] == "tpch");
    }

    [Fact]
    public async Task GetSchemaAsync_Catalogs_WithRestriction_FiltersToExactlyOneRow()
    {
        var table = await _connection.GetSchemaAsync("Catalogs", ["tpch"]);

        Assert.Single(table.Rows);
        Assert.Equal("tpch", (string)table.Rows[0][0]);
    }

    [Fact]
    public async Task GetSchemaAsync_Schemas_RestrictedToTpch_IncludesTiny()
    {
        var table = await _connection.GetSchemaAsync("Schemas", ["tpch", null]);

        Assert.True(table.Rows.Count > 0);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["schema_name"] == "tiny");
    }

    [Fact]
    public async Task GetSchemaAsync_Tables_RestrictedToTpchTiny_IncludesNation()
    {
        var table = await _connection.GetSchemaAsync("Tables", ["tpch", "tiny", null, null]);

        Assert.True(table.Rows.Count > 0);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["table_name"] == "nation");
    }

    [Fact]
    public async Task GetSchemaAsync_Tables_RestrictedToASingleTable_ReturnsExactlyOneRow()
    {
        var table = await _connection.GetSchemaAsync("Tables", ["tpch", "tiny", "nation", null]);

        Assert.Single(table.Rows);
        Assert.Equal("nation", (string)table.Rows[0]["table_name"]);
    }

    [Fact]
    public async Task GetSchemaAsync_Columns_RestrictedToNation_IncludesNationkey()
    {
        var table = await _connection.GetSchemaAsync("Columns", ["tpch", "tiny", "nation", null]);

        Assert.True(table.Rows.Count > 0);
        Assert.Contains(table.Rows.Cast<DataRow>(), r => (string)r["column_name"] == "nationkey");
    }

    [Fact]
    public async Task GetSchemaAsync_Views_RestrictedToTpch_RoundTripsWithoutError()
    {
        // tpch has no views, so this asserts the cross-catalog round trip does not throw rather
        // than that rows exist — Views on a connector with zero views is a legitimate result.
        var table = await _connection.GetSchemaAsync("Views", ["tpch", null, null]);
        Assert.NotNull(table);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task GetSchemaAsync_Functions_ReturnsBuiltInFunctions()
    {
        var table = await _connection.GetSchemaAsync("Functions");
        Assert.True(table.Rows.Count > 0);
    }

    [Fact]
    public async Task GetSchemaAsync_Functions_WithRestriction_FiltersByName()
    {
        var table = await _connection.GetSchemaAsync("Functions", ["abs"]);

        Assert.True(table.Rows.Count > 0);
        Assert.All(table.Rows.Cast<DataRow>(), r => Assert.Equal("abs", (string)r[0], ignoreCase: true));
    }

    [Fact]
    public async Task GetSchemaAsync_SessionProperties_ReturnsProperties()
    {
        var table = await _connection.GetSchemaAsync("SessionProperties");
        Assert.True(table.Rows.Count > 0);
    }

    [Fact]
    public async Task GetSchemaAsync_UnsupportedCollectionName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => _connection.GetSchemaAsync("NotARealCollection"));
}
