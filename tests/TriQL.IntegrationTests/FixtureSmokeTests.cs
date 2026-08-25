using System.Net.Http.Json;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// Throwaway fixture-only smoke test (P0-T12). Proves the Testcontainers fixture itself works,
/// using a raw <see cref="HttpClient"/> before any product code exists. Superseded by the real
/// <c>/v1/info</c> client tests in Phase 1 (P1-T21); this class is deleted once those land.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class FixtureSmokeTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task InfoEndpoint_ReturnsSuccess()
    {
        using var client = new HttpClient { BaseAddress = fixture.ServerUri };

        using var response = await client.GetAsync("v1/info");

        response.EnsureSuccessStatusCode();
        var info = await response.Content.ReadFromJsonAsync<TrinoInfoResponse>();
        Assert.NotNull(info);
    }

    private sealed record TrinoInfoResponse(bool Starting, NodeVersion NodeVersion);

    private sealed record NodeVersion(string Version);
}
