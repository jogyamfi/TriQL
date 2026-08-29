using System.Diagnostics;
using System.Net;
using TriQL.Client.Diagnostics;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Diagnostics;

/// <summary>
/// P6-T8: assert span shape (trino.query / trino.request) and traceparent propagation.
/// <see cref="ActivityListener"/> is process-global, so every test here uses a unique <see cref="TrinoSessionOptions.Server"/>
/// host and filters captured activities by <c>server.address</c> to stay correct even when other test
/// classes execute their own queries concurrently (xUnit runs test classes in parallel by default).
/// </summary>
public sealed class TracingTests
{
    [Fact]
    public async Task ExecuteAsync_EmitsQueryAndRequestSpans_WithExpectedTags()
    {
        var host = UniqueHost();
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (Equals(a.GetTagItem("server.address"), host)) { lock (activities) { activities.Add(a); } } },
        };
        ActivitySource.AddActivityListener(listener);

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions
        {
            Server = new Uri($"https://{host}/"),
            Catalog = "tpch",
            Schema = "tiny",
        };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var querySpan = Assert.Single(activities, a => a.OperationName == "trino.query");
        Assert.Equal("trino", querySpan.GetTagItem("db.system"));
        Assert.Equal("SELECT nationkey FROM tpch.tiny.nation", querySpan.GetTagItem("db.statement"));
        Assert.Equal("tpch", querySpan.GetTagItem("trino.catalog"));
        Assert.Equal("tiny", querySpan.GetTagItem("trino.schema"));
        Assert.Equal("q1", querySpan.GetTagItem("trino.query_id"));
        Assert.Equal(ActivityStatusCode.Unset, querySpan.Status);

        var requestSpan = Assert.Single(activities, a => a.OperationName == "trino.request");
        Assert.Equal("POST", requestSpan.GetTagItem("http.request.method"));
        Assert.Equal(200, requestSpan.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesTraceParent_OnOutboundRequests()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri($"https://{UniqueHost()}/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var request = Assert.Single(fake.ReceivedRequests);
        Assert.True(request.HasHeader("traceparent"));
    }

    [Fact]
    public async Task ExecuteAsync_RedactStatementInTraces_RedactsDbStatementTag()
    {
        var host = UniqueHost();
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (Equals(a.GetTagItem("server.address"), host)) { lock (activities) { activities.Add(a); } } },
        };
        ActivitySource.AddActivityListener(listener);

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri($"https://{host}/"), RedactStatementInTraces = true };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var querySpan = Assert.Single(activities, a => a.OperationName == "trino.query");
        Assert.Equal("***REDACTED***", querySpan.GetTagItem("db.statement"));
    }

    [Fact]
    public void NoListenerAttached_StartActivityReturnsNull()
    {
        // FR-11.2.4: the zero-cost path is StartActivity itself returning null; every helper tolerates that.
        Assert.Null(Tracing.StartQueryActivity("SELECT 1", null, null, null, redactStatement: false));
        Assert.Null(Tracing.StartRequestActivity(HttpMethod.Get, new Uri("https://trino.example.com/")));
    }

    [Fact]
    public async Task ExecuteAsync_ParameterizedQuery_TracesOriginalSql_NotBoundValues()
    {
        // Regression (SEC-1): the span used to be tagged with the submitted text, which for a
        // parameterized query is "EXECUTE triql_<guid> USING '<value>'" — leaking every parameter
        // value into telemetry and making db.statement unique per execution.
        var host = UniqueHost();
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Tracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (Equals(a.GetTagItem("server.address"), host)) { lock (activities) { activities.Add(a); } } },
        };
        ActivitySource.AddActivityListener(listener);

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri($"https://{host}/") };
        await using var client = new TrinoClient(options, invoker);

        var parameters = new TrinoParameterCollection { { "ssn", "123-45-6789" } };
        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM people WHERE ssn = :ssn", parameters);
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var querySpan = Assert.Single(activities, a => a.OperationName == "trino.query");
        var statement = Assert.IsType<string>(querySpan.GetTagItem("db.statement"));

        Assert.Equal("SELECT nationkey FROM people WHERE ssn = :ssn", statement);
        Assert.DoesNotContain("123-45-6789", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("EXECUTE", statement, StringComparison.Ordinal);

        // The value must still have reached the server, just not the trace tag.
        var submitted = Assert.Single(fake.ReceivedRequests, r => r.Method == HttpMethod.Post);
        Assert.Contains("123-45-6789", submitted.Body!, StringComparison.Ordinal);
    }

    private static string UniqueHost() => $"{Guid.NewGuid():N}.example.com";

    private static string Page(string id, string? nextUri, bool columns, string rows)
    {
        var nextUriJson = nextUri is null ? "null" : $"\"{nextUri}\"";
        var columnsJson = columns ? """[{"name":"nationkey","type":"bigint"}]""" : "null";
        return $$$"""
        {"id":"{{{id}}}","nextUri":{{{nextUriJson}}},"columns":{{{columnsJson}}},"data":[{{{rows}}}],"stats":{"state":"FINISHED","queued":false,"scheduled":true,"nodes":1,"totalSplits":1,"queuedSplits":0,"runningSplits":0,"completedSplits":1,"cpuTimeMillis":1,"wallTimeMillis":1,"queuedTimeMillis":0,"elapsedTimeMillis":1,"processedRows":1,"processedBytes":1,"physicalInputBytes":1,"peakMemoryBytes":1,"spilledBytes":0,"progressPercentage":100.0}}
        """;
    }
}

