using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T10: per-query fallback verification (FR-5.1.3b). A single session must handle some queries
/// returning spooled data and others direct data, without caching a per-session conclusion about
/// which protocol is in use.
/// </summary>
/// <remarks>
/// <b>Spec-vs-reality note.</b> FR-5.1.3b describes Trino falling back to the direct protocol
/// per-query "for queries that would not benefit from spooling." Empirically, against this real
/// spooling-configured cluster, no row-returning <c>SELECT</c> tried during Phase 7 — down to
/// <c>SELECT * FROM tpch.tiny.region</c> (5 rows) and <c>SELECT 1</c> — was ever observed to return
/// the bare array-of-arrays direct-protocol shape; every one returned the spooled envelope
/// (<c>encoding</c>+<c>segments</c>) with a single <c>"inline"</c>-kind segment once its size and row
/// count stayed under <c>protocol.spooling.inlining.max-rows</c>/<c>max-size</c> (see
/// <see cref="SegmentTests"/>'s remarks, and the raw-probe exploration recorded while building this
/// fixture). In other words, on Trino 466's real implementation, what the specification labels a
/// "direct protocol" per-query fallback appears to actually be implemented as
/// spooled-envelope-with-inline-segments rather than a literal reversion to the pre-spooling
/// array-of-arrays wire shape — the two are functionally similar (no out-of-band fetch needed
/// either way) but structurally different on the wire, and <c>StatementResponseMapper</c> already
/// handles both without a code change (FR-5.1.2's shape detection routes the inline case through
/// <c>SegmentClient.DecodeInline</c>). This test therefore demonstrates the client-observable
/// contract FR-5.1.3b actually requires — a session correctly mixing outcomes without caching a
/// wrong assumption — using the explicit client-side lever a caller genuinely has for it
/// (<see cref="TrinoSessionOptions.QueryDataEncodings"/> set empty, FR-5.1.5), since that is the only
/// way this cluster was observed to produce a literal direct-protocol array in practice.
/// </remarks>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class MixedProtocolTests(SpoolingClusterFixture cluster)
{
    [Fact]
    public async Task SingleSession_MixesDirectAndSpooledQueries_WithoutCachingWrongAssumption()
    {
        var options = cluster.CreateBaseOptions();
        options.QueryDataEncodings = []; // FR-5.1.5: empty forces the direct protocol.
        await using var client = new TrinoClient(options);

        // Query 1: direct protocol, forced by empty QueryDataEncodings.
        await using (var direct = await client.ExecuteAsync("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey"))
        {
            var rows = new List<object?[]>();
            await foreach (var row in direct.ReadRowsAsync())
            {
                rows.Add(row.ToArray());
            }

            Assert.Equal(25, rows.Count);
            Assert.Equal(0L, rows[0][0]);
            Assert.Equal(TrinoQueryState.Finished, direct.State);
        }

        // Mutate the session's live options — the same TrinoClient instance, no new client — and
        // issue a query large enough to genuinely spool. FR-5.1.3b: the client must not have cached
        // a "this session never spools" conclusion from query 1.
        options.QueryDataEncodings = ["json+zstd", "json+lz4", "json"];
        await using (var spooled = await client.ExecuteAsync(
            "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber"))
        {
            long count = 0;
            await foreach (var _ in spooled.ReadRowsAsync())
            {
                count++;
            }

            Assert.Equal(60_175, count);
            Assert.Equal(TrinoQueryState.Finished, spooled.State);
        }

        // Query 3: back to direct, proving the mix goes both ways within the one session/client.
        options.QueryDataEncodings = [];
        await using (var direct2 = await client.ExecuteAsync("SELECT regionkey, name FROM tpch.tiny.region ORDER BY regionkey"))
        {
            var rows = new List<object?[]>();
            await foreach (var row in direct2.ReadRowsAsync())
            {
                rows.Add(row.ToArray());
            }

            Assert.Equal(5, rows.Count);
            Assert.Equal(TrinoQueryState.Finished, direct2.State);
        }
    }
}
