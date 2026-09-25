using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// P7-T5: negative control. Every other test in Lane B depends on this guarantee — if the spooling
/// cluster silently fell back to the direct protocol (the exact false-pass risk the Phase 7 plan
/// calls out for P7-T2), this test must fail, not merely fail to notice.
/// </summary>
/// <remarks>
/// Row-count correctness alone cannot be the negative control: FR-5.1.3's fallback is defined to be
/// transparent, so a misconfigured cluster serving direct-protocol data would still return correct
/// rows through <see cref="TrinoClient"/> — the exact false pass this test exists to prevent. The
/// distinguishing signal instead comes from a raw HTTP probe issued directly against the
/// coordinator's <c>v1/statement</c> endpoint, bypassing <c>TriQL.Client</c> entirely, that inspects
/// the literal shape of the <c>data</c> member per FR-5.1.2: an object carrying <c>encoding</c> and
/// <c>segments</c> proves the spooled envelope was genuinely selected; an array-of-arrays would fail
/// the assertion outright. The query is shaped deliberately large — <c>tpch.tiny.lineitem</c>'s
/// ~60,175 rows comfortably exceeds Trino's inlining thresholds
/// (<c>protocol.spooling.inlining.max-rows=1000</c>, <c>...max-size=128kB</c>, both defaults) — so
/// this is not a coin flip. Only once that hard gate passes does the test go on to prove
/// <see cref="TrinoClient"/> itself consumes that same genuinely-spooled data correctly end to end.
/// </remarks>
[Collection(SpoolingClusterCollection.Name)]
[Trait("Category", "Spooling")]
public sealed class SpoolingNegativeControlTests(SpoolingClusterFixture cluster)
{
    private const string Query = "SELECT orderkey, linenumber, quantity FROM tpch.tiny.lineitem ORDER BY orderkey, linenumber";
    private const long ExpectedRowCount = 60_175;
    private static readonly string[] SupportedEncodings = ["json", "json+lz4", "json+zstd"];

    [Fact]
    public async Task RawProtocolProbe_ConfirmsSpooledEnvelopeShape_NotDirectProtocol()
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler) { BaseAddress = cluster.ServerUri, Timeout = TimeSpan.FromSeconds(30) };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/statement")
        {
            Content = new StringContent(Query, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Trino-User", "triql-negative-control");
        request.Headers.Add("X-Trino-Query-Data-Encoding", "json+zstd,json+lz4,json");

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var dataShape = await FindFirstNonEmptyDataShapeAsync(client, body);

        Assert.Equal(JsonValueKind.Object, dataShape.ValueKind);
        Assert.True(dataShape.TryGetProperty("encoding", out var encoding), "Spooled envelope must carry 'encoding'.");
        Assert.True(dataShape.TryGetProperty("segments", out _), "Spooled envelope must carry 'segments'.");
        Assert.Contains(encoding.GetString(), SupportedEncodings);
    }

    [Fact]
    public async Task LargeQuery_AgainstSpoolingCluster_ReturnsCorrectRowsThroughClient()
    {
        var options = cluster.CreateBaseOptions();
        await using var client = new TrinoClient(options);

        await using var resultSet = await client.ExecuteAsync(Query);

        long count = 0;
        object?[]? first = null;
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            if (first is null)
            {
                first = row.ToArray();
            }

            count++;
        }

        Assert.Equal(ExpectedRowCount, count);
        Assert.NotNull(first);
        Assert.Equal(1L, first![0]);
        Assert.Equal(TrinoQueryState.Finished, resultSet.State);
    }

    /// <summary>Follows <c>nextUri</c> until a page carries a non-null <c>data</c> member, per the statement protocol's own pagination.</summary>
    private static async Task<JsonElement> FindFirstNonEmptyDataShapeAsync(HttpClient client, JsonElement body)
    {
        var current = body;
        while (true)
        {
            if (current.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.TryGetProperty("message", out var message) ? message.GetString() : "Query failed.");
            }

            if (current.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
            {
                return data;
            }

            if (!current.TryGetProperty("nextUri", out var nextUriProp))
            {
                throw new InvalidOperationException("Query completed with no 'data' member on any page — cannot determine protocol shape.");
            }

            using var response = await client.GetAsync(nextUriProp.GetString());
            response.EnsureSuccessStatusCode();
            current = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }
}
