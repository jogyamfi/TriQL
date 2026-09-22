using TriQL.Client;
using TriQL.Client.Exceptions;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P3-T17: parameterized queries via <c>EXECUTE … USING</c> (FR-8.1–FR-8.6), and the prepared
/// statement lifecycle including deallocation on execution (FR-8.9), against a real container.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class ParametersAndPreparedStatementTests(TrinoContainerFixture fixture)
{
    private static TrinoClient CreateClient(TrinoContainerFixture fixture) =>
        new(new TrinoSessionOptions { Server = fixture.ServerUri });

    private static async Task<TrinoRow> ExecuteSingleRowAsync(TrinoClient client, string sql, TrinoParameterCollection? parameters = null, bool retainPreparedStatement = false)
    {
        await using var resultSet = await client.ExecuteAsync(sql, parameters, retainPreparedStatement);

        var rows = new List<TrinoRow>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.Clone());
        }

        Assert.Single(rows);
        return rows[0];
    }

    [Fact]
    public async Task PositionalParameters_AreBoundInCollectionOrder()
    {
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add(5);
        parameters.Add(7);

        var row = await ExecuteSingleRowAsync(client, "SELECT ? + ?", parameters);

        Assert.Equal(12L, row.GetInt64(0));
    }

    [Fact]
    public async Task NamedParameters_AreRewrittenAndBoundByName()
    {
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add("first", 10);
        parameters.Add("second", 32);

        var row = await ExecuteSingleRowAsync(client, "SELECT :first + :second", parameters);

        Assert.Equal(42L, row.GetInt64(0));
    }

    [Fact]
    public async Task AtSignNamedParameters_AreRewrittenAndBoundByName()
    {
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add("name", "Trino");

        var row = await ExecuteSingleRowAsync(client, "SELECT concat('Hello, ', @name)", parameters);

        Assert.Equal("Hello, Trino", row.GetString(0));
    }

    [Fact]
    public async Task StringParameter_ContainingQuotesAndSqlKeywords_IsBoundAsData_NotExecutedAsSql()
    {
        // Classic injection payload: if the value leaked into the statement text unescaped, this
        // would either break parsing or alter statement structure. Bound as a parameter, it must
        // come back byte-for-byte as the string value (FR-8.3, SEC-4).
        const string payload = "'; DROP TABLE users; --";
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add(payload);

        var row = await ExecuteSingleRowAsync(client, "SELECT ?", parameters);

        Assert.Equal(payload, row.GetString(0));
    }

    [Fact]
    public async Task ParameterCountMismatch_ThrowsBeforeAnyNetworkCall()
    {
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add(1);

        await Assert.ThrowsAsync<TrinoParameterException>(
            () => client.ExecuteAsync("SELECT ? + ?", parameters));
    }

    [Fact]
    public async Task ExecuteAsync_WithParameters_DeallocatesThePreparedStatement_ByDefault()
    {
        await using var client = CreateClient(fixture);
        Assert.Empty(client.Session.PreparedStatements);

        var parameters = new TrinoParameterCollection();
        parameters.Add(1);
        await using (await client.ExecuteAsync("SELECT ?", parameters))
        {
        }

        // FR-8.9: deallocated on completion unless the caller opts into retention.
        Assert.Empty(client.Session.PreparedStatements);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetainPreparedStatement_KeepsItRegisteredOnTheSession()
    {
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add(1);

        await using (await client.ExecuteAsync("SELECT ?", parameters, retainPreparedStatement: true))
        {
        }

        var retained = Assert.Single(client.Session.PreparedStatements);
        Assert.StartsWith("triql_", retained.Key, StringComparison.Ordinal);
        Assert.Equal("SELECT ?", retained.Value);
    }

    [Fact]
    public async Task RetainedPreparedStatement_DoesNotBreakSubsequentQueries()
    {
        // The coordinator does not retain prepared-statement state across requests (see
        // TrinoSession.RegisterPreparedStatement), so the client resends every entry still in
        // Session.PreparedStatements via X-Trino-Prepared-Statement on every subsequent submission.
        // Confirm a retained statement from an earlier call doesn't break a later, unrelated query.
        await using var client = CreateClient(fixture);
        var parameters = new TrinoParameterCollection();
        parameters.Add(1);
        await using (await client.ExecuteAsync("SELECT ?", parameters, retainPreparedStatement: true))
        {
        }

        Assert.Single(client.Session.PreparedStatements);

        var row = await ExecuteSingleRowAsync(client, "SELECT 41 + 1");

        Assert.Equal(42, row.GetInt32(0));
    }

    [Fact]
    public async Task PrepareExecuteLifecycle_MultipleExecutions_EachDeallocateIndependently()
    {
        await using var client = CreateClient(fixture);

        for (var i = 0; i < 3; i++)
        {
            var parameters = new TrinoParameterCollection();
            parameters.Add(i);

            var row = await ExecuteSingleRowAsync(client, "SELECT ? * 2", parameters);
            Assert.Equal((long)(i * 2), row.GetInt64(0));
        }

        // Every one of the three prepared statements (each with a fresh GUID-suffixed name) must
        // have been deallocated; none should have leaked into session bookkeeping.
        Assert.Empty(client.Session.PreparedStatements);
    }
}
