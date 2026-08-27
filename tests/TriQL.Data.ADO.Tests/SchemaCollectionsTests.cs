using System.Net;
using TriQL.Client;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO.Tests.Fakes;

namespace TriQL.Data.ADO.Tests;

public sealed class SchemaCollectionsTests
{
    [Fact]
    public async Task GetSchemaAsync_MetaDataCollections_DoesNotRequireAnOpenConnection()
    {
        using var connection = new TrinoConnection("Host=h;");
        var table = await connection.GetSchemaAsync();

        Assert.Equal("MetaDataCollections", table.TableName);
        Assert.Contains(table.Rows.Cast<System.Data.DataRow>(), r => (string)r["CollectionName"] == "Tables");
    }

    [Fact]
    public async Task GetSchemaAsync_DataTypes_IncludesEveryFR72BaseType()
    {
        using var connection = new TrinoConnection("Host=h;");
        var table = await connection.GetSchemaAsync("DataTypes");

        var typeNames = table.Rows.Cast<System.Data.DataRow>().Select(r => (string)r["TypeName"]).ToList();
        Assert.Contains("bigint", typeNames);
        Assert.Contains("decimal", typeNames);
        Assert.Contains("array", typeNames);
        Assert.Contains("row", typeNames);
    }

    [Fact]
    public async Task GetSchemaAsync_Restrictions_ListsRestrictionsForEveryCollection()
    {
        using var connection = new TrinoConnection("Host=h;");
        var table = await connection.GetSchemaAsync("Restrictions");

        Assert.Contains(table.Rows.Cast<System.Data.DataRow>(), r => (string)r["CollectionName"] == "Tables" && (string)r["RestrictionName"] == "catalog");
    }

    [Fact]
    public async Task GetSchemaAsync_UnsupportedCollectionName_Throws()
    {
        using var fake = new FakeTrinoCoordinator();
        using var connection = await OpenConnectionAsync(fake);

        await Assert.ThrowsAsync<ArgumentException>(() => connection.GetSchemaAsync("NotARealCollection"));
    }

    [Fact]
    public async Task GetSchemaAsync_TooManyRestrictions_Throws()
    {
        using var fake = new FakeTrinoCoordinator();
        using var connection = await OpenConnectionAsync(fake);

        await Assert.ThrowsAsync<ArgumentException>(() => connection.GetSchemaAsync("Tables", ["a", "b", "c", "d", "e"]));
    }

    [Fact]
    public async Task GetSchemaAsync_Tables_BindsSchemaRestrictionAsParameter_NotConcatenated()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":[{"name":"table_name","type":"varchar"}],"data":[]}""");

        using var connection = await OpenConnectionAsync(fake);

        const string injectionPayload = "sch'; DROP TABLE users; --";
        await connection.GetSchemaAsync("Tables", ["hive", injectionPayload, null, null]);

        var submitted = fake.ReceivedRequests.Single(r => r.Method == HttpMethod.Post);
        var preparedHeader = System.Net.WebUtility.UrlDecode(submitted.HeaderValues("X-Trino-Prepared-Statement").Single());

        // The generated SQL text (sent decoupled from the value, in a header) must keep the
        // restriction as a `?` placeholder: the raw payload must never alter the query structure.
        Assert.Contains("table_schema = ?", preparedHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(injectionPayload, preparedHeader, StringComparison.Ordinal);

        // The value itself travels only inside the audited, escaped EXECUTE ... USING literal.
        Assert.NotNull(submitted.Body);
        Assert.Contains("EXECUTE", submitted.Body, StringComparison.Ordinal);
        Assert.Contains("''", submitted.Body, StringComparison.Ordinal);
    }

    private static async Task<TrinoConnection> OpenConnectionAsync(FakeTrinoCoordinator fake)
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));
        await connection.OpenAsync();
        return connection;
    }
}
