using System.Net;
using System.Text;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests.Spooling;

/// <summary>
/// Fake-coordinator spooling suite (P5-T14): inline and spooled segments, out-of-order concurrent
/// arrival delivered in <c>rowOffset</c> order (FR-5.2.4/5.2.5), metadata mismatches (FR-5.2.6),
/// unknown-encoding rejection (FR-5.1.4), and credential scoping for off-origin segment hosts.
/// Exercises the whole pipeline (<see cref="TrinoClient.ExecuteAsync"/> through
/// <c>ReadRowsAsync</c>), not just <c>SegmentClient</c> in isolation, so a wiring mistake in
/// <c>StatementClient.ReadEnvelopeAsync</c> would fail these too.
/// </summary>
public sealed class SpoolingTests
{
    private static TrinoSessionOptions Options(ITrinoAuthenticator? authenticator = null) => new()
    {
        Server = new Uri("https://trino.example.com/"),
        Authenticator = authenticator,
        PollingBackoffInitialDelay = TimeSpan.FromMilliseconds(1),
        PollingBackoffMaxDelay = TimeSpan.FromMilliseconds(1),
    };

    [Fact]
    public async Task InlineSegments_OutOfArrayOrder_DeliveredInRowOffsetOrder()
    {
        using var fake = new FakeTrinoCoordinator();
        var seg0 = InlineSegment(rowOffset: 0, rows: "[[1],[2]]", rowsCount: 2);
        var seg1 = InlineSegment(rowOffset: 2, rows: "[[3],[4]]", rowsCount: 2);
        // seg1 (higher rowOffset) listed first in the wire array to prove delivery follows rowOffset, not array order.
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [seg1, seg0]));

        using var invoker = fake.CreateInvoker();
        await using var client = new TrinoClient(Options(), invoker);
        await using var resultSet = await client.ExecuteAsync("SELECT n FROM t");

        var values = new List<long>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            values.Add(row.GetInt64(0));
        }

        Assert.Equal([1L, 2L, 3L, 4L], values);
    }

    [Fact]
    public async Task SpooledSegments_SlowerLowerOffsetSegment_StillDeliveredFirst()
    {
        using var fake = new FakeTrinoCoordinator();
        var lowUri = new Uri("https://objectstore.example.com/seg/low");
        var highUri = new Uri("https://objectstore.example.com/seg/high");
        var lowAck = new Uri("https://trino.example.com/v1/spooling/ack/low");
        var highAck = new Uri("https://trino.example.com/v1/spooling/ack/high");

        var lowPayload = Encoding.UTF8.GetBytes("[[1],[2]]");
        var highPayload = Encoding.UTF8.GetBytes("[[3],[4]]");

        // The lower-rowOffset segment resolves *after* the higher one (a short delay simulates
        // out-of-order network completion) — final delivery order must still be by rowOffset.
        fake.RouteAsync(lowUri, async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(30), ct).ConfigureAwait(false);
            return Fixed(HttpStatusCode.OK, lowPayload);
        });
        fake.RouteFixed(highUri, HttpStatusCode.OK, highPayload);
        fake.RouteFixed(lowAck, HttpStatusCode.OK);
        fake.RouteFixed(highAck, HttpStatusCode.OK);

        var segLow = SpooledSegment(rowOffset: 0, uri: lowUri, ackUri: lowAck, segmentSize: lowPayload.Length, rowsCount: 2);
        var segHigh = SpooledSegment(rowOffset: 2, uri: highUri, ackUri: highAck, segmentSize: highPayload.Length, rowsCount: 2);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [segLow, segHigh]));

        using var invoker = fake.CreateInvoker();
        var options = Options();
        options.SegmentFetchParallelism = 4;
        await using var client = new TrinoClient(options, invoker);
        await using var resultSet = await client.ExecuteAsync("SELECT n FROM t");

        var values = new List<long>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            values.Add(row.GetInt64(0));
        }

        Assert.Equal([1L, 2L, 3L, 4L], values);
    }

    [Fact]
    public async Task SpooledSegments_AckRequestsSentAfterFetch()
    {
        using var fake = new FakeTrinoCoordinator();
        var uri = new Uri("https://objectstore.example.com/seg/0");
        var ackUri = new Uri("https://trino.example.com/v1/spooling/ack/0");
        var payload = Encoding.UTF8.GetBytes("[[1]]");

        fake.RouteFixed(uri, HttpStatusCode.OK, payload);
        fake.RouteFixed(ackUri, HttpStatusCode.OK);

        var seg = SpooledSegment(rowOffset: 0, uri: uri, ackUri: ackUri, segmentSize: payload.Length, rowsCount: 1);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [seg]));

        using var invoker = fake.CreateInvoker();
        await using var client = new TrinoClient(Options(), invoker);
        await using (var resultSet = await client.ExecuteAsync("SELECT n FROM t"))
        {
            await foreach (var _ in resultSet.ReadRowsAsync())
            {
            }
        }

        // The ack is fire-and-forget; give the background task a bounded chance to land before asserting.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!fake.ReceivedRequests.Any(r => r.RequestUri == ackUri) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Contains(fake.ReceivedRequests, r => r.RequestUri == ackUri && r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task SegmentSizeMismatch_ThrowsTrinoProtocolException()
    {
        using var fake = new FakeTrinoCoordinator();
        // Declares a base64 payload of 9 bytes but metadata.segmentSize says 999.
        var seg = InlineSegmentWithExplicitSize(rowOffset: 0, rows: "[[1],[2]]", rowsCount: 2, segmentSize: 999);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [seg]));

        using var invoker = fake.CreateInvoker();
        await using var client = new TrinoClient(Options(), invoker);

        // The fake's only response is both the submission response and the final page (nextUri:
        // null), so segment resolution — and the mismatch it raises — happens synchronously inside
        // ExecuteAsync's SubmitAsync call, before a TrinoResultSet is even returned (see
        // StatementClient.ReadEnvelopeAsync, which resolves pending spooling on every page it reads).
        var ex = await Assert.ThrowsAsync<TrinoProtocolException>(() => client.ExecuteAsync("SELECT n FROM t"));

        Assert.Contains("segmentSize", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RowsCountMismatch_ThrowsTrinoProtocolException()
    {
        using var fake = new FakeTrinoCoordinator();
        // metadata declares rowsCount 5 but the payload only contains 2 rows.
        var seg = InlineSegment(rowOffset: 0, rows: "[[1],[2]]", rowsCount: 5);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [seg]));

        using var invoker = fake.CreateInvoker();
        await using var client = new TrinoClient(Options(), invoker);

        var ex = await Assert.ThrowsAsync<TrinoProtocolException>(() => client.ExecuteAsync("SELECT n FROM t"));

        Assert.Contains("rowsCount", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownEncoding_ThrowsNamingTheEncoding()
    {
        using var fake = new FakeTrinoCoordinator();
        var seg = InlineSegment(rowOffset: 0, rows: "[[1]]", rowsCount: 1);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json+brotli", [seg]));

        using var invoker = fake.CreateInvoker();
        await using var client = new TrinoClient(Options(), invoker);

        var ex = await Assert.ThrowsAsync<TrinoProtocolException>(() => client.ExecuteAsync("SELECT n FROM t"));

        Assert.Contains("json+brotli", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SegmentFetch_CredentialAttachedOnlyForCoordinatorProxiedSegment()
    {
        using var fake = new FakeTrinoCoordinator();
        var sameOriginUri = new Uri("https://trino.example.com/v1/spooling/segment/same");
        var offOriginUri = new Uri("https://objectstore.example.com/seg/off");
        var sameOriginAck = new Uri("https://trino.example.com/v1/spooling/ack/same");
        var offOriginAck = new Uri("https://trino.example.com/v1/spooling/ack/off"); // ackUri is always same-origin per G3

        var payload = Encoding.UTF8.GetBytes("[[1]]");
        fake.RouteFixed(sameOriginUri, HttpStatusCode.OK, payload);
        fake.RouteFixed(offOriginUri, HttpStatusCode.OK, payload);
        fake.RouteFixed(sameOriginAck, HttpStatusCode.OK);
        fake.RouteFixed(offOriginAck, HttpStatusCode.OK);

        var segSame = SpooledSegment(rowOffset: 0, uri: sameOriginUri, ackUri: sameOriginAck, segmentSize: payload.Length, rowsCount: 1);
        var segOff = SpooledSegment(rowOffset: 1, uri: offOriginUri, ackUri: offOriginAck, segmentSize: payload.Length, rowsCount: 1);
        fake.Enqueue(HttpStatusCode.OK, FinalSpooledPage("json", [segSame, segOff]));

        using var invoker = fake.CreateInvoker();
        using var authenticator = new JwtAuthenticator("secret-token");
        await using var client = new TrinoClient(Options(authenticator), invoker);
        await using var resultSet = await client.ExecuteAsync("SELECT n FROM t");
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
        }

        var sameOriginRequest = fake.ReceivedRequests.Single(r => r.RequestUri == sameOriginUri);
        var offOriginRequest = fake.ReceivedRequests.Single(r => r.RequestUri == offOriginUri);

        Assert.True(sameOriginRequest.HasHeader("Authorization"));
        Assert.False(offOriginRequest.HasHeader("Authorization"));
    }

    private static HttpResponseMessage Fixed(HttpStatusCode statusCode, byte[] body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(body) };
        response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        return response;
    }

    private static string FinalSpooledPage(string encoding, IEnumerable<string> segments) =>
        $$"""
        {"id":"q1","nextUri":null,"columns":[{"name":"n","type":"bigint"}],"data":{"encoding":"{{encoding}}","segments":[{{string.Join(",", segments)}}] } }
        """;

    private static string InlineSegment(long rowOffset, string rows, long rowsCount)
    {
        var bytes = Encoding.UTF8.GetBytes(rows);
        return InlineSegmentWithExplicitSize(rowOffset, rows, rowsCount, bytes.Length);
    }

    private static string InlineSegmentWithExplicitSize(long rowOffset, string rows, long rowsCount, long segmentSize)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rows));
        return $$"""
        {"type":"inline","metadata":{"rowOffset":{{rowOffset}},"rowsCount":{{rowsCount}},"segmentSize":{{segmentSize}}},"data":"{{base64}}"}
        """;
    }

    private static string SpooledSegment(long rowOffset, Uri uri, Uri ackUri, long segmentSize, long rowsCount) =>
        $$"""
        {"type":"spooled","metadata":{"rowOffset":{{rowOffset}},"rowsCount":{{rowsCount}},"segmentSize":{{segmentSize}}},"uri":"{{uri}}","ackUri":"{{ackUri}}"}
        """;
}
