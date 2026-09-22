using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T9: off-origin URI verification against the closed G3 policy, at the real-topology level.
/// <c>UriGuard</c>'s accept/reject logic is already unit-tested in
/// <c>tests/TriQL.Client.Tests/Security/UriGuardTests.cs</c> against synthetic URIs; the point here
/// is proving the *real* topology this fixture builds — segment <c>uri</c> legitimately pointing at
/// MinIO (a different host and port than the coordinator), <c>ackUri</c> staying on the
/// coordinator's own origin — is exactly the shape <c>UriGuard</c> was built to permit, end to end,
/// with no SSRF exception thrown for the legitimate case.
/// </summary>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class UriGuardTests(SpoolingClusterFixture cluster)
{
    [Fact]
    public async Task RealSpooledQuery_HasOffOriginSegmentUris_AndSameOriginAckUris()
    {
        var segments = await RawProtocolProbe.CollectSegmentsAsync(
            cluster, "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber");

        var spooled = segments.FindAll(s => s.Type == "spooled");
        Assert.NotEmpty(spooled);

        var coordinatorOrigin = cluster.ServerUri;
        foreach (var segment in spooled)
        {
            Assert.NotNull(segment.SegmentUri);
            Assert.NotNull(segment.AckUri);

            // Segment payload URI is legitimately off-origin object storage (MinIO), not the
            // coordinator — this is the case UriGuard.ValidateSegmentUri must permit.
            var segmentIsOffOrigin = !string.Equals(segment.SegmentUri!.Host, coordinatorOrigin.Host, StringComparison.OrdinalIgnoreCase)
                || segment.SegmentUri.Port != coordinatorOrigin.Port;
            Assert.True(segmentIsOffOrigin, $"Expected the segment uri '{segment.SegmentUri}' to be off-origin from the coordinator '{coordinatorOrigin}'.");
            Assert.Equal("https", segment.SegmentUri.Scheme);

            // ackUri stays on the coordinator's own origin exactly — the case
            // UriGuard.ValidateAckUri requires and would reject if it were ever off-origin.
            Assert.Equal(coordinatorOrigin.Scheme, segment.AckUri!.Scheme, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(coordinatorOrigin.Host, segment.AckUri.Host, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(coordinatorOrigin.Port, segment.AckUri.Port);
        }
    }

    /// <summary>
    /// The same real topology, now proven end to end through the actual client: no
    /// <c>TrinoProtocolException</c> from <c>UriGuard</c> rejecting the legitimate off-origin
    /// segment host or the legitimate same-origin ack host, for a query that genuinely spools.
    /// </summary>
    [Fact]
    public async Task RealClient_AcceptsRealTopology_WithNoSsrfException()
    {
        var options = cluster.CreateBaseOptions();
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync(
            "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber");

        long count = 0;
        await foreach (var _ in resultSet.ReadRowsAsync())
        {
            count++;
        }

        Assert.Equal(60_175, count);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }
}
