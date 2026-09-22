using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T6: the P5-T16 manual validation checklist, automated against the real spooling cluster —
/// all three encodings, a large multi-segment result, and row-order correctness across segment
/// boundaries (FR-5.2.4). P7-T5 already established that <c>tpch.tiny.lineitem</c>'s ~60,175 rows
/// genuinely spool (multiple real <c>"spooled"</c>-kind segments, confirmed via the raw protocol
/// probe used while building this fixture) rather than falling back to direct or staying inline, so
/// this class reuses that query shape for the per-encoding checks and a larger one for the
/// multi-segment/order check.
/// </summary>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class SegmentTests(SpoolingClusterFixture cluster)
{
    [Theory]
    [InlineData("json")]
    [InlineData("json+lz4")]
    [InlineData("json+zstd")]
    public async Task EachEncoding_DecodesRealSpooledSegmentsCorrectly(string encoding)
    {
        var options = cluster.CreateBaseOptions();
        options.QueryDataEncodings = [encoding];
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync(
            "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber");

        long count = 0;
        long? previousOrderKey = null;
        int? previousLineNumber = null;
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            var orderKey = (long)row[0]!;
            var lineNumber = (int)row[1]!;

            if (previousOrderKey is { } prevOrder)
            {
                var inOrder = orderKey > prevOrder || (orderKey == prevOrder && lineNumber > previousLineNumber!.Value);
                Assert.True(inOrder, $"Row order violated at count {count}: ({prevOrder},{previousLineNumber}) -> ({orderKey},{lineNumber}).");
            }

            previousOrderKey = orderKey;
            previousLineNumber = lineNumber;
            count++;
        }

        Assert.Equal(60_175, count);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }

    /// <summary>
    /// A large enough result (<c>tpch.sf1.orders</c>, 1,500,000 rows) to span many segments given
    /// Trino's 16MB default <c>protocol.spooling.max-segment-size</c> — exercises concurrent segment
    /// fetch (<c>SegmentFetchParallelism</c>, default 4) while still verifying strict row-order
    /// delivery (FR-5.2.4) across every segment boundary, not just within one.
    /// </summary>
    [Fact]
    public async Task LargeMultiSegmentResult_PreservesRowOrderAcrossSegmentBoundaries()
    {
        var options = cluster.CreateBaseOptions();
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync("SELECT orderkey FROM tpch.sf1.orders ORDER BY orderkey");

        long count = 0;
        long? previous = null;
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            var orderKey = (long)row[0]!;
            if (previous is { } prev)
            {
                Assert.True(orderKey >= prev, $"Row order violated at count {count}: {prev} -> {orderKey}.");
            }

            previous = orderKey;
            count++;
        }

        Assert.Equal(1_500_000, count);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }
}
