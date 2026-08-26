using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// Walking skeleton (P1-T18, throwaway). Submits a hardcoded <c>SELECT</c>, follows <c>nextUri</c>
/// in a naive loop against a real container, and proves the statement-protocol round trip end to
/// end before any production streaming machinery exists. Deleted by <c>P2-T4</c>.
/// </summary>
/// <remarks>
/// <b>Findings recorded during this exercise:</b>
/// <list type="bullet">
/// <item>The initial <c>POST /v1/statement</c> response for <c>tpch.tiny.nation</c> already
/// contains a schema-bearing page with rows for this dataset size; <c>nextUri</c> polling still
/// runs at least once more before completion (absence of <c>nextUri</c> signals completion, not
/// row presence — confirms FR-4.2.4/FR-4.3.4).</item>
/// <item>Intermediate pages can have zero rows in <c>data</c> while still carrying a <c>nextUri</c>;
/// the naive loop below simply keeps polling, matching FR-4.3.4's requirement that a
/// no-row page not terminate enumeration.</item>
/// <item>The default <c>trinodb/trino</c> image ships a working <c>tpch</c> catalog with no
/// additional configuration, so integration tests can rely on <c>tpch.tiny.nation</c> without a
/// custom catalog file.</item>
/// </list>
/// </remarks>
[Collection(TrinoContainerCollection.Name)]
public sealed class WalkingSkeletonTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task SelectQuery_FollowsNextUri_AndReturnsAllRows()
    {
        using var client = new HttpClient { BaseAddress = fixture.ServerUri };
        var rows = new List<JsonElement>();

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, "v1/statement")
        {
            Content = new StringContent("SELECT nationkey, name FROM tpch.tiny.nation ORDER BY nationkey", Encoding.UTF8, "text/plain"),
        };
        submitRequest.Headers.Add("X-Trino-User", "triql-walking-skeleton");

        using var submitResponse = await client.SendAsync(submitRequest);
        submitResponse.EnsureSuccessStatusCode();
        var page = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();

        var nextUri = GetNextUri(page);
        CollectRows(page, rows);

        while (nextUri is not null)
        {
            using var pollResponse = await client.GetAsync(nextUri);
            pollResponse.EnsureSuccessStatusCode();
            page = await pollResponse.Content.ReadFromJsonAsync<JsonElement>();
            nextUri = GetNextUri(page);
            CollectRows(page, rows);
        }

        Assert.Equal(25, rows.Count);
    }

    private static string? GetNextUri(JsonElement page) =>
        page.TryGetProperty("nextUri", out var nextUriProp) ? nextUriProp.GetString() : null;

    private static void CollectRows(JsonElement page, List<JsonElement> rows)
    {
        if (page.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            rows.AddRange(data.EnumerateArray());
        }
    }
}
