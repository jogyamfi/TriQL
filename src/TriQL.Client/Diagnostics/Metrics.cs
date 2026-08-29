using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace TriQL.Client.Diagnostics;

/// <summary>
/// The <c>TriQL.Client</c> <see cref="Meter"/> and its instruments (FR-11.2.1). Kept internal:
/// listeners attach by meter/instrument name via <see cref="MeterListener"/> or an OpenTelemetry
/// exporter, never through a reference to this type. Every instrument call is a documented no-op
/// fast path when no listener is attached (FR-11.2.4), so callers pay nothing for unused telemetry.
/// </summary>
internal static class Metrics
{
    public const string MeterName = "TriQL.Client";

    // Declared explicitly so this type is not `beforefieldinit`: without it the runtime may defer
    // field initialization until the first static *field* access, so EnsureInitialized() below
    // would not actually publish the instruments.
    static Metrics()
    {
    }

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> QueriesSubmitted = Meter.CreateCounter<long>("triql.queries.submitted");
    public static readonly Counter<long> QueriesFailed = Meter.CreateCounter<long>("triql.queries.failed");
    public static readonly Counter<long> QueriesCancelled = Meter.CreateCounter<long>("triql.queries.cancelled");
    public static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("triql.query.duration", unit: "ms");
    public static readonly Histogram<double> TimeToFirstRow = Meter.CreateHistogram<double>("triql.query.time_to_first_row", unit: "ms");
    public static readonly Counter<long> RowsRead = Meter.CreateCounter<long>("triql.rows.read");
    public static readonly Counter<long> BytesReceived = Meter.CreateCounter<long>("triql.bytes.received");
    public static readonly Counter<long> PagesReceived = Meter.CreateCounter<long>("triql.pages.received");
    public static readonly Counter<long> HttpRetries = Meter.CreateCounter<long>("triql.http.retries");

    /// <summary>
    /// The live read-ahead buffers contributing to <c>triql.buffer.occupancy</c>. Keys are weak, so a
    /// buffer that is garbage-collected without being disposed drops out on its own; each value must
    /// therefore capture the buffer's <c>ByteBudgetGate</c> rather than the buffer itself, or the
    /// entry would keep its own key alive forever.
    /// </summary>
    private static readonly ConditionalWeakTable<object, Func<long>> BufferOccupancyProviders = [];

    // Summing live buffers (rather than maintaining a running total) is what makes the gauge
    // self-correcting: an abandoned result set still holding buffered pages simply stops being
    // counted once it is disposed or collected, instead of leaking its bytes into the total forever.
    private static readonly ObservableGauge<long> BufferOccupancy =
        Meter.CreateObservableGauge("triql.buffer.occupancy", ObserveBufferOccupancy, unit: "bytes");

    /// <summary>Adds a live read-ahead buffer to the <c>triql.buffer.occupancy</c> gauge.</summary>
    public static void RegisterBufferOccupancyProvider(object key, Func<long> occupiedBytes) =>
        BufferOccupancyProviders.AddOrUpdate(key, occupiedBytes);

    /// <summary>Removes a read-ahead buffer from the <c>triql.buffer.occupancy</c> gauge.</summary>
    public static void UnregisterBufferOccupancyProvider(object key) => BufferOccupancyProviders.Remove(key);

    /// <summary>Forces this type's instruments to be published, so a listener started later observes them.</summary>
    public static void EnsureInitialized()
    {
    }

    private static long ObserveBufferOccupancy()
    {
        long total = 0;
        foreach (var (_, occupiedBytes) in BufferOccupancyProviders)
        {
            total += occupiedBytes();
        }

        return total;
    }
}

