using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P5-T15: proves FR-5.1.3 against a real, non-spooling-configured coordinator. The container this
/// fixture starts has no <c>protocol.spooling.enabled</c>/object-storage configuration (FR-5.1.3a),
/// so a query requesting the full spooled-encoding preference list MUST still return correct,
/// direct-protocol results with no error and no client-side configuration change — proving that
/// negotiation degrades transparently on a real server that simply ignores the
/// <c>X-Trino-Query-Data-Encoding</c> header. This does not, and cannot, prove the spooled path
/// itself works against a real coordinator; that is Phase 7's job (risk X11/X4). Named distinctly
/// from other integration test files being added concurrently elsewhere in the working tree.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class SpoolingFallbackTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task RequestingSpooledEncodings_AgainstUnconfiguredServer_FallsBackToDirectProtocolTransparently()
    {
        var options = new TrinoSessionOptions
        {
            Server = fixture.ServerUri,
            // The FR-1.1.1-specified 1.1 default preference list, requested explicitly here since
            // 1.0's actual default is empty (FR-5.1.6/G7) — this test exercises what happens when a
            // caller opts in against a server that hasn't enabled spooling.
            QueryDataEncodings = ["json+zstd", "json+lz4", "json"],
        };
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey");

        var columns = await resultSet.WaitForSchemaAsync();
        Assert.Equal(["nationkey", "name"], columns.Select(c => c.Name));

        var rows = new List<object?[]>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.ToArray());
        }

        Assert.Equal(25, rows.Count);
        Assert.Equal(0L, rows[0][0]);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }

    [Fact]
    public async Task RequestingSpooledEncodings_LargerScan_StillCompletesCorrectly()
    {
        // A heavier query against the same unconfigured server, exercising the negotiation-per-page
        // path (FR-5.1.3b: the client must not cache a per-session "this server never spools"
        // conclusion) across however many nextUri pages the coordinator happens to use, rather than
        // only the small single-page case above.
        var options = new TrinoSessionOptions
        {
            Server = fixture.ServerUri,
            QueryDataEncodings = ["json+zstd", "json+lz4", "json"],
        };
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync("SELECT count(*) AS c FROM tpch.sf1.lineitem");

        var rows = new List<object?[]>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.ToArray());
        }

        Assert.Single(rows);
        Assert.Equal(6_001_215L, rows[0][0]);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }
}
