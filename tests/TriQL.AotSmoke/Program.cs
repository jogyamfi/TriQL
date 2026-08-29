// P6-T9 / TEST-12: a trimming/Native-AOT smoke application. Publishing this project with
// PublishAot=true must produce zero trim/AOT warnings (NFR-COMPAT-3). It always exercises the
// hot JSON/type-conversion/ADO.NET paths offline against an in-process fake handler so it is
// useful even without a live coordinator; when TRINO_TEST_SERVER is set (e.g. in an environment
// with Docker/a real cluster) it additionally round-trips a real query against that server.
using System.Data.Common;
using System.Net;
using System.Text;
using TriQL.Client;
using TriQL.Data.ADO;

try
{
    await RunOfflineSmokeAsync();

    var liveServer = Environment.GetEnvironmentVariable("TRINO_TEST_SERVER");
    if (!string.IsNullOrEmpty(liveServer))
    {
        await RunLiveSmokeAsync(liveServer);
    }
    else
    {
        Console.WriteLine("TRINO_TEST_SERVER not set; skipping the live-coordinator smoke check.");
    }

    Console.WriteLine("AOT smoke checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"AOT smoke checks FAILED: {ex}");
    return 1;
}

static async Task RunOfflineSmokeAsync()
{
    using var fake = new FakePage(
        """{"id":"aot-smoke-1","nextUri":null,"columns":[{"name":"n","type":"bigint"},{"name":"s","type":"varchar"}],"data":[[1,"one"],[2,"two"]],"stats":{"state":"FINISHED","queued":false,"scheduled":true,"nodes":1,"totalSplits":1,"queuedSplits":0,"runningSplits":0,"completedSplits":1,"cpuTimeMillis":1,"wallTimeMillis":1,"queuedTimeMillis":0,"elapsedTimeMillis":1,"processedRows":2,"processedBytes":2,"physicalInputBytes":2,"peakMemoryBytes":2,"spilledBytes":0,"progressPercentage":100.0}}""");
    using var invoker = new HttpMessageInvoker(fake, disposeHandler: false);

    var options = new TrinoSessionOptions { Server = new Uri("https://smoke.invalid/") };
    await using var client = new TrinoClient(options, invoker);
    await using var resultSet = await client.ExecuteAsync("SELECT n, s FROM smoke");

    var rowCount = 0;
    await foreach (var row in resultSet.ReadRowsAsync())
    {
        _ = row.GetInt64(0);
        _ = row.GetString(1);
        rowCount++;
    }

    if (rowCount != 2)
    {
        throw new InvalidOperationException($"Expected 2 rows from the offline fake, got {rowCount}.");
    }

    // Exercise the ADO.NET surface: connection-string round trip and provider factory wiring
    // (FR-1.3, FR-9.4), both reflection-sensitive areas under trimming.
    var builder = new TrinoConnectionStringBuilder { Server = options.Server!.ToString(), Catalog = "tpch", Schema = "tiny" };
    var roundTripped = new TrinoConnectionStringBuilder(builder.ConnectionString);
    if (roundTripped.Catalog != "tpch" || roundTripped.Schema != "tiny")
    {
        throw new InvalidOperationException("Connection-string round trip did not preserve Catalog/Schema.");
    }

    DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance);
    var factory = DbProviderFactories.GetFactory(TrinoProviderFactory.InvariantName);
    using var connection = factory.CreateConnection() ?? throw new InvalidOperationException("Factory returned no connection.");
    var parameter = factory.CreateParameter() ?? throw new InvalidOperationException("Factory returned no parameter.");
}

static async Task RunLiveSmokeAsync(string server)
{
    var options = new TrinoSessionOptions { Server = new Uri(server), Catalog = "tpch", Schema = "tiny" };
    await using var client = new TrinoClient(options);
    var info = await client.GetServerInfoAsync();
    Console.WriteLine($"Connected to live Trino server version {info.Version}.");

    await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation ORDER BY nationkey LIMIT 5");
    var rowCount = 0;
    await foreach (var _ in resultSet.ReadRowsAsync())
    {
        rowCount++;
    }

    if (rowCount != 5)
    {
        throw new InvalidOperationException($"Expected 5 rows from the live coordinator, got {rowCount}.");
    }
}

/// <summary>A minimal single-response fake coordinator, kept local so this project has no test-project dependency.</summary>
internal sealed class FakePage(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
}

