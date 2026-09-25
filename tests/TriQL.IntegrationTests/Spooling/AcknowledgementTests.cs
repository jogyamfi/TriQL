using Amazon.S3.Model;
using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T8: acknowledgement verification. Confirms <c>ackUri</c> actually releases object-storage
/// space (FR-5.2.3) and that cancellation triggers the best-effort ack sweep (FR-5.2.7).
/// </summary>
/// <remarks>
/// <b>Scope decision — the "failed ack degrades to a warning" case.</b> The Phase 7 task brief
/// permits reasoning about coverage here instead of constructing a live failure, and that is what
/// this class does: forcing a real ack request to fail against a real coordinator without also
/// failing the query itself (which depends on the same connectivity) is not practically
/// constructible black-box — the segment <c>uri</c> data fetch and the coordinator-proxied
/// <c>ackUri</c> both traverse the same network paths this suite controls, so breaking one breaks
/// the other. Deliberately corrupting an <c>ackUri</c> mid-flight would require either a
/// man-in-the-middle on the client's own traffic (which is the exact SSRF-adjacent shape P7-T9's
/// <c>UriGuard</c> exists to prevent building test infrastructure that emulates) or stopping a
/// container mid-query, which would nondeterministically also fail in-flight segment fetches rather
/// than isolate the ack path alone. That failure mode is instead covered at the unit level in
/// <c>tests/TriQL.Client.Tests/Spooling/SegmentAcknowledgerTests.cs</c>, which mocks the transport
/// specifically to make the ack request fail while everything else succeeds — exactly the isolation
/// a real server cannot give this suite. This class covers what a real server uniquely can: that a
/// *successful* ack genuinely deletes the object, and that cancellation genuinely triggers the sweep.
/// </remarks>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class AcknowledgementTests(SpoolingClusterFixture cluster)
{
    private const string Query = "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber";

    [Fact]
    public async Task FullyConsumedQuery_AcknowledgesEverySpooledSegment_AndBucketShrinksBack()
    {
        using var s3 = cluster.Minio.CreateS3Client();

        var before = await CountObjectsAsync(s3);

        var options = cluster.CreateBaseOptions();
        await using (var client = new TrinoClient(options))
        {
            await using var resultSet = await client.ExecuteAsync(Query);
            await foreach (var _ in resultSet.ReadRowsAsync())
            {
                // Fully drain: FR-5.2.3 schedules each segment's acknowledgement right after that
                // segment's rows are handed to the consumer, so draining the whole result set is
                // what triggers every ack.
            }
        }

        // Acknowledgement is fire-and-forget relative to consumer latency (FR-5.2.3), so give the
        // in-flight acks — and the coordinator's own object deletion — a bounded window to land.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        long after;
        do
        {
            after = await CountObjectsAsync(s3);
            if (after <= before)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        while (DateTime.UtcNow < deadline);

        Assert.True(
            after <= before,
            $"Expected the bucket's object count to return to its pre-query baseline ({before}) after consumption; it was still {after} after the wait.");
    }

    [Fact]
    public async Task CancellingBeforeFullDrain_StillTriggersAckSweepCleanup()
    {
        using var s3 = cluster.Minio.CreateS3Client();

        var before = await CountObjectsAsync(s3);

        var options = cluster.CreateBaseOptions();
        await using (var client = new TrinoClient(options))
        {
            var resultSet = await client.ExecuteAsync("SELECT orderkey FROM tpch.sf1.orders ORDER BY orderkey");
            var rowsSeen = 0;
            await foreach (var _ in resultSet.ReadRowsAsync())
            {
                rowsSeen++;
                // Read well past the first several segments' worth of rows before abandoning, so
                // multiple segments are fully decoded and handed to the consumer (i.e. genuinely
                // ack-eligible per FR-5.2.3) before DisposeAsync cancels the pump and runs the
                // best-effort ack sweep (FR-5.2.7, bounded by FR-4.6.2) — a cancel after only a
                // handful of rows mostly exercises segments still mid-fetch, which legitimately
                // have no ack to sweep at all (no rows from them ever reached the consumer).
                if (rowsSeen >= 100_000)
                {
                    break;
                }
            }

            await resultSet.DisposeAsync();
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        long after;
        do
        {
            after = await CountObjectsAsync(s3);
            if (after <= before + 1)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        while (DateTime.UtcNow < deadline);

        // Allow exactly one straggler: with SegmentFetchParallelism (default 4), up to a handful of
        // segments can be mid-fetch — not yet decoded, so never reaching FR-5.2.3's "rows handed to
        // the consumer" trigger for scheduling an ack — at the instant DisposeAsync cancels the
        // pump. FR-5.2.7 itself only requires the sweep "where an ack can still be issued cheaply";
        // a segment whose fetch was itself cancelled mid-flight has no completed acknowledgeable
        // outcome to sweep, and ages out via the coordinator's own fs.segment.ttl instead.
        Assert.True(
            after <= before + 1,
            $"Expected cancellation's best-effort ack sweep to bring the bucket back near its pre-query baseline ({before}, +1 straggler tolerance); it was still {after} after the wait.");
    }

    private async Task<long> CountObjectsAsync(Amazon.S3.AmazonS3Client s3)
    {
        long count = 0;
        string? continuationToken = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = cluster.Minio.BucketName,
                ContinuationToken = continuationToken,
            });
            count += page.S3Objects?.Count ?? 0;
            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        return count;
    }
}
