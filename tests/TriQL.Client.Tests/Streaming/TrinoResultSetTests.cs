using System.Net;
using TriQL.Client.Exceptions;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Streaming;

public sealed class TrinoResultSetTests
{
    [Fact]
    public async Task ReadRowsAsync_YieldsRowsInOrder_AcrossMultiplePages()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: "http://coordinator/next/1", columns: true, rows: "[1],[2]"));
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: false, rows: "[3]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");

        var values = new List<object?>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            values.Add(row.GetValue(0));
        }

        Assert.Equal([1L, 2L, 3L], values);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }

    [Fact]
    public async Task WaitForSchemaAsync_CompletesFromTheInitialPage_WithoutReadingARow()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        var columns = await resultSet.WaitForSchemaAsync();

        Assert.Equal("nationkey", Assert.Single(columns).Name);
    }

    [Fact]
    public async Task Progress_IsRaisedWithLatestStats_ForEachPage()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");

        var progressCount = 0;
        resultSet.Progress += (_, args) =>
        {
            progressCount++;
            Assert.Equal("FINISHED", args.Stats.State);
        };

        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        Assert.Equal(1, progressCount);
    }

    [Fact]
    public async Task Cancellation_SendsDelete_AndRaisesOperationCanceledException()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: "http://coordinator/next/1", columns: true, rows: "[1]"));
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: "http://coordinator/next/2", columns: false, rows: "[]"), delay: TimeSpan.FromSeconds(5));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            PollingBackoffInitialDelay = TimeSpan.FromMilliseconds(1),
            PollingBackoffMaxDelay = TimeSpan.FromMilliseconds(1),
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");

        using var cts = new CancellationTokenSource();

        var enumerationTask = Task.Run(async () =>
        {
            await foreach (var _ in resultSet.ReadRowsAsync(cts.Token))
            {
                await cts.CancelAsync();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enumerationTask);
        Assert.Equal(TrinoQueryState.Cancelled, resultSet.State);
        Assert.Contains(fake.ReceivedRequests, r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task DrainAsync_ExposesUpdateTypeAndUpdateCount_ForNonQueryStatements()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(
            HttpStatusCode.OK,
            """
            {"id":"q1","nextUri":null,"updateType":"CREATE TABLE","updateCount":0,"data":null}
            """);

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("CREATE TABLE t (x bigint)");

        Assert.False(resultSet.IsQuery);

        var summary = await resultSet.DrainAsync();

        Assert.Equal("CREATE TABLE", summary.UpdateType);
        Assert.Equal(0, summary.UpdateCount);
    }

    [Fact]
    public async Task QueryTimeout_RaisesTrinoTimeoutException_DuringSubmission()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"), delay: TimeSpan.FromSeconds(5));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            QueryTimeout = TimeSpan.FromMilliseconds(50),
        };
        await using var client = new TrinoClient(options, invoker);

        var exception = await Assert.ThrowsAsync<TrinoTimeoutException>(() => client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation"));

        Assert.Equal(TimeSpan.FromMilliseconds(50), exception.ConfiguredTimeout);
    }

    [Fact]
    public async Task QueryTimeout_RaisesTrinoTimeoutException_DuringAdvanceLoop_AndCancelsServerSide()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: "http://coordinator/next/1", columns: true, rows: "[1]"));
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: false, rows: "[2]"), delay: TimeSpan.FromSeconds(5));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri("https://trino.example.com/"),
            QueryTimeout = TimeSpan.FromMilliseconds(100),
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");

        var exception = await Assert.ThrowsAsync<TrinoTimeoutException>(async () =>
        {
            await foreach (var _ in resultSet.ReadRowsAsync())
            {
            }
        });

        Assert.Equal(TimeSpan.FromMilliseconds(100), exception.ConfiguredTimeout);
        Assert.Equal(TrinoQueryState.TimedOut, resultSet.State);
        Assert.Contains(fake.ReceivedRequests, r => r.Method == HttpMethod.Delete);
    }

    private static string Page(string id, string? nextUri, bool columns, string rows)
    {
        var nextUriJson = nextUri is null ? "null" : $"\"{nextUri}\"";
        var columnsJson = columns ? """[{"name":"nationkey","type":"bigint"}]""" : "null";
        return $$$"""
        {"id":"{{{id}}}","nextUri":{{{nextUriJson}}},"columns":{{{columnsJson}}},"data":[{{{rows}}}],"stats":{"state":"FINISHED","queued":false,"scheduled":true,"nodes":1,"totalSplits":1,"queuedSplits":0,"runningSplits":0,"completedSplits":1,"cpuTimeMillis":1,"wallTimeMillis":1,"queuedTimeMillis":0,"elapsedTimeMillis":1,"processedRows":1,"processedBytes":1,"physicalInputBytes":1,"peakMemoryBytes":1,"spilledBytes":0,"progressPercentage":100.0}}
        """;
    }
}
