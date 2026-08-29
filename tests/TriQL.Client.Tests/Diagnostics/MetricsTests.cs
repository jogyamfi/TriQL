using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using TriQL.Client.Diagnostics;
using TriQL.Client.Internal;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Diagnostics;

/// <summary>P6-T8: assert instrument emission for the ten FR-11.2.1 instruments.</summary>
public sealed class MetricsTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulQuery_EmitsExpectedInstruments()
    {
        // Instruments are process-global (System.Diagnostics.Metrics), and xUnit runs other test
        // classes concurrently, so every measurement is filtered down to THIS test's own unique
        // query id (tagged as trino.query_id on every per-query instrument) to stay deterministic.
        var queryId = $"q-{Guid.NewGuid():N}";
        var measurements = new List<(string Instrument, object? Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Metrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (HasQueryId(tags, queryId))
            {
                lock (measurements) { measurements.Add((instrument.Name, value)); }
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (HasQueryId(tags, queryId))
            {
                lock (measurements) { measurements.Add((instrument.Name, value)); }
            }
        });
        listener.Start();

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: queryId, nextUri: null, columns: true, rows: "[1],[2]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        // No arbitrary wait needed (TEST-13): the background pump records duration/time-to-first-row
        // and disposes its query Activity strictly before it completes the buffer, and completing the
        // buffer is what lets this foreach above return.
        var names = measurements.Select(m => m.Instrument).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("triql.queries.submitted", names);
        Assert.Contains("triql.query.duration", names);
        Assert.Contains("triql.query.time_to_first_row", names);
        Assert.Contains("triql.rows.read", names);
        Assert.Contains("triql.bytes.received", names);
        Assert.Contains("triql.pages.received", names);

        var rowsRead = measurements.Where(m => m.Instrument == "triql.rows.read").Sum(m => (long)m.Value!);
        Assert.Equal(2, rowsRead);
    }

    private static bool HasQueryId(ReadOnlySpan<KeyValuePair<string, object?>> tags, string queryId)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "trino.query_id" && Equals(tag.Value, queryId))
            {
                return true;
            }
        }

        return false;
    }

    // The occupancy gauge is a process-wide aggregate and xUnit runs other test classes (which
    // create their own PageBuffers) in parallel, so these tests deliberately work at a
    // megabyte scale: every other buffer in the suite is a few hundred bytes, which makes the
    // assertions below insensitive to that concurrent noise without being time- or order-dependent.
    private const long LargeBytes = 8L * 1024 * 1024;
    private const long NoiseCeilingBytes = 1024 * 1024;

    [Fact]
    public async Task BufferOccupancyGauge_CountsLiveBuffer_AndStopsCountingItOnceDisposed()
    {
        // MeterListener.Start() only sees instruments already published, so the Metrics static
        // ctor must have run first; without this the test passes or fails purely on whether some
        // other test happened to touch Metrics earlier in the process.
        Metrics.EnsureInitialized();

        long? observed = null;
        using var listener = CreateOccupancyListener(value => observed = value);

        var buffer = new PageBuffer(budgetBytes: 64L * 1024 * 1024);
        await buffer.EnqueueAsync(EmptyPage(), LargeBytes, CancellationToken.None);

        listener.RecordObservableInstruments();
        Assert.True(observed >= LargeBytes, $"Expected the live buffer's {LargeBytes} bytes to be counted, but the gauge read {observed}.");

        buffer.Dispose();

        listener.RecordObservableInstruments();
        Assert.True(observed < NoiseCeilingBytes, $"Expected the disposed buffer to stop being counted, but the gauge still read {observed}.");
    }

    [Fact]
    public async Task BufferOccupancyGauge_DoesNotLeak_WhenResultSetIsAbandonedBeforeReading()
    {
        // Regression: occupancy used to be a running global total incremented on enqueue and
        // decremented only on read, so every result set disposed before being drained (cancellation,
        // an early `break`, TrinoDataReader.Close(), a failed query) permanently inflated the gauge.
        Metrics.EnsureInitialized();

        long? observed = null;
        using var listener = CreateOccupancyListener(value => observed = value);

        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, LargeVarcharPage("abandoned-1"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        var resultSet = await client.ExecuteAsync("SELECT payload FROM big");
        await resultSet.DisposeAsync();

        listener.RecordObservableInstruments();
        Assert.True(
            observed < NoiseCeilingBytes,
            $"An abandoned result set leaked its buffered pages into the gauge, which read {observed}.");
    }

    private static MeterListener CreateOccupancyListener(Action<long> onMeasurement)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Metrics.MeterName && instrument.Name == "triql.buffer.occupancy")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => onMeasurement(value));
        listener.Start();
        return listener;
    }

    /// <summary>A single row whose one varchar column estimates to roughly 2 MB (26 + 2 bytes per char).</summary>
    private static string LargeVarcharPage(string id)
    {
        var payload = new string('a', 1_000_000);
        return $$$"""
        {"id":"{{{id}}}","nextUri":null,"columns":[{"name":"payload","type":"varchar"}],"data":[["{{{payload}}}"]],"stats":{"state":"FINISHED","queued":false,"scheduled":true,"nodes":1,"totalSplits":1,"queuedSplits":0,"runningSplits":0,"completedSplits":1,"cpuTimeMillis":1,"wallTimeMillis":1,"queuedTimeMillis":0,"elapsedTimeMillis":1,"processedRows":1,"processedBytes":1,"physicalInputBytes":1,"peakMemoryBytes":1,"spilledBytes":0,"progressPercentage":100.0}}
        """;
    }

    private static TrinoPage EmptyPage() => new([], [], valuesAreDecoded: false, null, null, null);

    [Fact]
    public async Task NoListenerAttached_DoesNotThrow()
    {
        // FR-11.2.4: instrument calls must be safe/cheap with nothing subscribed. This does not
        // measure allocation, only that the unlistened fast path behaves correctly end to end.
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, Page(id: "q1", nextUri: null, columns: true, rows: "[1]"));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey FROM tpch.tiny.nation");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
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
