using System.Net;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests;

/// <summary>EF0-T1: <see cref="TrinoSessionOptions.ParameterBinding"/> selects how parameterized statement text is sent.</summary>
public sealed class ParameterBindingTests
{
    private const string FinishedPage = """{"id":"q1","nextUri":null,"columns":[{"name":"x","type":"integer"}],"data":[[1]]}""";

    private static async Task<CapturedRequest> SubmitAsync(TrinoParameterBinding binding, string sql, bool retainPreparedStatement = false)
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, FinishedPage);
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), ParameterBinding = binding };
        await using var client = new TrinoClient(options, invoker);
        var parameters = new TrinoParameterCollection();
        parameters.Add("id", 42);
        parameters.Add("name", "O'Brien");

        await using (var resultSet = await client.ExecuteAsync(sql, parameters, retainPreparedStatement))
        {
            await resultSet.DrainAsync();
        }

        return fake.ReceivedRequests[0];
    }

    [Fact]
    public async Task PreparedStatementHeader_IsTheDefault_AndSendsTheStatementInAHeader()
    {
        Assert.Equal(TrinoParameterBinding.PreparedStatementHeader, new TrinoSessionOptions().ParameterBinding);

        var request = await SubmitAsync(TrinoParameterBinding.PreparedStatementHeader, "SELECT x FROM t WHERE id = :id AND name = :name");

        Assert.True(request.HasHeader("X-Trino-Prepared-Statement"));
        Assert.StartsWith("EXECUTE triql_", request.Body, StringComparison.Ordinal);
        Assert.EndsWith(" USING 42, 'O''Brien'", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteImmediate_SendsTheStatementInTheBody_AndNoPreparedStatementHeader()
    {
        var request = await SubmitAsync(TrinoParameterBinding.ExecuteImmediate, "SELECT x FROM t WHERE id = :id AND name = :name");

        Assert.False(request.HasHeader("X-Trino-Prepared-Statement"));
        Assert.Equal("EXECUTE IMMEDIATE 'SELECT x FROM t WHERE id = ? AND name = ?' USING 42, 'O''Brien'", request.Body);
    }

    [Fact]
    public async Task ExecuteImmediate_EscapesQuotesInTheStatementText()
    {
        var request = await SubmitAsync(TrinoParameterBinding.ExecuteImmediate, "SELECT x FROM t WHERE tag = 'it''s' AND id = :id AND name = :name -- 'note'");

        Assert.Equal("EXECUTE IMMEDIATE 'SELECT x FROM t WHERE tag = ''it''''s'' AND id = ? AND name = ? -- ''note''' USING 42, 'O''Brien'", request.Body);
    }

    [Fact]
    public async Task ExecuteImmediate_WithRetainPreparedStatement_UsesTheHeaderPath()
    {
        var request = await SubmitAsync(TrinoParameterBinding.ExecuteImmediate, "SELECT x FROM t WHERE id = :id AND name = :name", retainPreparedStatement: true);

        Assert.True(request.HasHeader("X-Trino-Prepared-Statement"));
        Assert.StartsWith("EXECUTE triql_", request.Body, StringComparison.Ordinal);
    }
}
