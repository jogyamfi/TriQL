using System.Data;
using System.Net;
using TriQL.Client;
using TriQL.Client.Exceptions;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO.Tests.Fakes;

namespace TriQL.Data.ADO.Tests;

/// <summary>
/// Regression tests for the defects found by the Phase 4 cross-check. Each test failed before its
/// corresponding fix.
/// </summary>
public sealed class CrossCheckRegressionTests
{
    private static readonly TimeSpan HangBudget = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteReader_OnDdlStatementWithNoColumns_CompletesInsteadOfHanging()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null,"updateType":"INSERT","updateCount":7}""");

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (1)";

        using var reader = await WithinBudgetAsync(command.ExecuteReaderAsync());

        Assert.Equal(0, reader.FieldCount);
        Assert.False(reader.HasRows);
        Assert.Equal(7, reader.RecordsAffected);
    }

    [Fact]
    public async Task ExecuteReader_OnQueryFailingBeforeSchema_SurfacesTheErrorInsteadOfHanging()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null,"error":{"message":"boom","errorCode":1,"errorName":"SYNTAX_ERROR","errorType":"USER_ERROR"}}""");

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT bad syntax";

        var reader = command.ExecuteReaderAsync();
        var completed = await Task.WhenAny(reader, Task.Delay(HangBudget));
        Assert.Same(reader, completed);

        var ex = await Assert.ThrowsAsync<TrinoQueryException>(() => reader);
        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForSchemaAsync_OnDdlStatement_CompletesWithNoColumns()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null,"updateType":"CREATE TABLE"}""");

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);
        await using var resultSet = await client.ExecuteAsync("CREATE TABLE t (x bigint)");

        var columns = await WithinBudgetAsync(resultSet.WaitForSchemaAsync());

        Assert.Empty(columns);
    }

    [Fact]
    public async Task WaitForSchemaAsync_OnFailingQuery_FaultsInsteadOfHanging()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null,"error":{"message":"boom","errorCode":1,"errorName":"SYNTAX_ERROR","errorType":"USER_ERROR"}}""");

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);
        await using var resultSet = await client.ExecuteAsync("SELECT bad syntax");

        var schema = resultSet.WaitForSchemaAsync();
        var completed = await Task.WhenAny(schema, Task.Delay(HangBudget));
        Assert.Same(schema, completed);
        await Assert.ThrowsAsync<TrinoQueryException>(() => schema);
    }

    [Fact]
    public async Task WaitForSchemaAsync_CompletesFromTheProducer_WithoutAnyConsumerReadingAPage()
    {
        using var fake = new FakeTrinoCoordinator();
        // The schema only arrives on the SECOND page, so nothing but the background pump can publish it.
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":"https://trino.example.com/v1/statement/q1/1","columns":null,"data":null}""");
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":[{"name":"x","type":"bigint"}],"data":[[1]]}""");

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);
        await using var resultSet = await client.ExecuteAsync("SELECT x FROM t");

        var columns = await WithinBudgetAsync(resultSet.WaitForSchemaAsync());

        Assert.Equal("x", Assert.Single(columns).Name);
    }

    [Fact]
    public async Task ServerVersion_OnAnOpenConnection_FetchesFromInfoAndCachesPerConnection()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"nodeVersion":{"version":"466"},"environment":"test","coordinator":true,"starting":false}""");

        using var connection = await OpenAsync(fake);

        Assert.Equal("466", connection.ServerVersion);

        // FR-9.1.4: cached per connection - a second read must not issue another /v1/info request.
        Assert.Equal("466", connection.ServerVersion);
        Assert.Single(fake.ReceivedRequests, r => r.RequestUri!.AbsolutePath.EndsWith("/v1/info", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerVersion_OnAClosedConnection_Throws()
    {
        using var connection = new TrinoConnection("Host=h;");
        Assert.Throws<InvalidOperationException>(() => connection.ServerVersion);
    }

    [Fact]
    public async Task InfoMessage_IsRaisedWithQueryStats()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, PageWithStats());

        using var connection = await OpenAsync(fake);
        var received = new List<TrinoInfoMessageEventArgs>();
        connection.InfoMessage += (_, e) => received.Add(e);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT x FROM t";
        await command.ExecuteNonQueryAsync();

        var message = Assert.Single(received);
        Assert.NotNull(message.Stats);
        Assert.Equal("FINISHED", message.Stats.State);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task InfoMessage_IsRaisedWithTheQueryError()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":[{"name":"x","type":"bigint"}],"data":null,"error":{"message":"boom","errorCode":1,"errorName":"SYNTAX_ERROR","errorType":"USER_ERROR"}}""");

        using var connection = await OpenAsync(fake);
        var received = new List<TrinoInfoMessageEventArgs>();
        connection.InfoMessage += (_, e) => received.Add(e);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT bad syntax";
        await Assert.ThrowsAsync<TrinoQueryException>(() => command.ExecuteNonQueryAsync());

        Assert.Contains(received, m => m.Error is TrinoQueryException);
    }

    [Fact]
    public async Task Accessors_AfterClose_Throw()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page("[1]"));

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT x FROM t";
        var reader = await command.ExecuteReaderAsync();
        Assert.True(reader.Read());
        reader.Close();

        Assert.Equal(0, reader.FieldCount);
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetInt64(0));
    }

    [Fact]
    public async Task HasRows_StaysTrue_AfterReadingPastTheLastRow()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page("[1]"));

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT x FROM t";
        using var reader = await command.ExecuteReaderAsync();

        Assert.True(reader.HasRows);
        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.True(reader.HasRows);
    }

    [Fact]
    public async Task HasRows_IsFalse_ForAnEmptyResultSet()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(""));

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT x FROM t WHERE false";
        using var reader = await command.ExecuteReaderAsync();

        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
    }

    [Fact]
    public async Task CloseAsync_CancelsAnOutstandingQueryServerSide()
    {
        using var fake = new FakeTrinoCoordinator();
        // A never-ending query: every page points at another nextUri, so it is still running at Close().
        for (var i = 0; i < 50; i++)
        {
            fake.Enqueue(
                HttpStatusCode.OK,
                $$"""{"id":"q1","nextUri":"https://trino.example.com/v1/statement/q1/{{i + 1}}","columns":[{"name":"x","type":"bigint"}],"data":[[{{i}}]]}""",
                delay: TimeSpan.FromMilliseconds(20));
        }

        using var connection = await OpenAsync(fake);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT x FROM big";
        var reader = await command.ExecuteReaderAsync();
        Assert.True(reader.Read());

        await connection.CloseAsync();

        // FR-9.1.8: the DELETE proves the query was cancelled server-side, not merely abandoned.
        var deadline = DateTime.UtcNow + HangBudget;
        while (DateTime.UtcNow < deadline && !fake.ReceivedRequests.Any(r => r.Method == HttpMethod.Delete))
        {
            await Task.Delay(25);
        }

        Assert.Contains(fake.ReceivedRequests, r => r.Method == HttpMethod.Delete);
    }

    private static async Task<T> WithinBudgetAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(HangBudget));
        Assert.Same(task, completed);
        return await task;
    }

    private static async Task<TrinoConnection> OpenAsync(FakeTrinoCoordinator fake)
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));
        await connection.OpenAsync();
        return connection;
    }

    private static string Page(string rows) =>
        $$"""{"id":"q1","nextUri":null,"columns":[{"name":"x","type":"bigint"}],"data":[{{rows}}]}""";

    private static string PageWithStats() =>
        """{"id":"q1","nextUri":null,"columns":[{"name":"x","type":"bigint"}],"data":[[1]],"stats":{"state":"FINISHED","queued":false,"scheduled":true,"nodes":1,"totalSplits":1,"queuedSplits":0,"runningSplits":0,"completedSplits":1,"cpuTimeMillis":1,"wallTimeMillis":1,"queuedTimeMillis":0,"elapsedTimeMillis":1,"processedRows":1,"processedBytes":1,"physicalInputBytes":1,"peakMemoryBytes":1,"spilledBytes":0,"progressPercentage":100.0}}""";
}
