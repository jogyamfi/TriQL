using System.Net;
using TriQL.Client.Auth;
using TriQL.Client.Internal;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Spooling;

/// <summary>
/// Deterministic <see cref="SegmentAcknowledger"/> tests (P5-T13): fire-and-forget, retry on
/// transient failure, and failure-tolerance (FR-5.2.3), decoupled from the full pipeline's timing so
/// the assertions are not racing the fire-and-forget task.
/// </summary>
public sealed class SegmentAcknowledgerTests
{
    private static readonly Uri CoordinatorOrigin = new("https://trino.example.com/");

    [Fact]
    public async Task Acknowledge_Success_IsTrackedThenDrainsImmediately()
    {
        using var fake = new FakeTrinoCoordinator();
        var ackUri = new Uri("https://trino.example.com/v1/spooling/ack/1");
        fake.RouteFixed(ackUri, HttpStatusCode.OK);

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = CoordinatorOrigin };
        var acknowledger = new SegmentAcknowledger(logger: null);

        acknowledger.Acknowledge(ackUri, segmentHeaders: null, invoker, options, AnonymousAuthenticator.Instance, CoordinatorOrigin);
        await acknowledger.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(fake.ReceivedRequests, r => r.RequestUri == ackUri && r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Acknowledge_RetriesOnTransientFailure_ThenSucceeds()
    {
        using var fake = new FakeTrinoCoordinator();
        var ackUri = new Uri("https://trino.example.com/v1/spooling/ack/1");

        var attempt = 0;
        fake.Route(ackUri, _ =>
        {
            attempt++;
            return attempt < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = CoordinatorOrigin };
        var acknowledger = new SegmentAcknowledger(logger: null);

        acknowledger.Acknowledge(ackUri, segmentHeaders: null, invoker, options, AnonymousAuthenticator.Instance, CoordinatorOrigin);
        await acknowledger.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task Acknowledge_FailsPermanently_LogsWarningWithoutThrowing()
    {
        using var fake = new FakeTrinoCoordinator();
        var ackUri = new Uri("https://trino.example.com/v1/spooling/ack/1");
        fake.Route(ackUri, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = CoordinatorOrigin };
        using var loggerFactory = new CapturingLoggerFactory();
        var acknowledger = new SegmentAcknowledger(loggerFactory.Logger);

        // Must not throw: FR-5.2.3 requires acknowledgement failure to never fail the query.
        acknowledger.Acknowledge(ackUri, segmentHeaders: null, invoker, options, AnonymousAuthenticator.Instance, CoordinatorOrigin);
        await acknowledger.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(loggerFactory.Logger.Messages, m => m.Contains("cknowledg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DrainAsync_NoPendingAcknowledgements_ReturnsImmediately()
    {
        var acknowledger = new SegmentAcknowledger(logger: null);
        await acknowledger.DrainAsync(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DrainAsync_BoundsWaitEvenWhenAcknowledgementNeverCompletes()
    {
        using var fake = new FakeTrinoCoordinator();
        var ackUri = new Uri("https://trino.example.com/v1/spooling/ack/1");
        // Never enqueue/route a response: every attempt exhausts the retry policy's max attempts
        // against 404s (terminal, not retried) after RequestExecutor times out — regardless, DrainAsync
        // itself must return once its own timeout elapses rather than waiting indefinitely.
        using var invoker = fake.CreateInvoker();
        var options = new TrinoSessionOptions { Server = CoordinatorOrigin };
        var acknowledger = new SegmentAcknowledger(logger: null);

        acknowledger.Acknowledge(ackUri, segmentHeaders: null, invoker, options, AnonymousAuthenticator.Instance, CoordinatorOrigin);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await acknowledger.DrainAsync(TimeSpan.FromMilliseconds(200));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"DrainAsync took {sw.Elapsed}, expected to be bounded near its 200ms timeout.");
    }
}
