using System.Net;
using System.Net.Http.Headers;
using Azure.Core;
using TriQL.Client.Auth;

namespace TriQL.Client.Auth.Tests;

/// <summary>Contract tests for P6-T4: acquisition, caching, skew refresh, 401 refresh, single-flight.</summary>
public sealed class EntraIdAuthenticatorTests
{
    [Fact]
    public void Constructor_NoScopes_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EntraIdAuthenticator([]));
    }

    [Fact]
    public async Task ApplyAsync_AcquiresAndSendsToken()
    {
        var credential = FakeTokenCredential.AlwaysReturning("entra-token-1", TimeSpan.FromHours(1));
        using var authenticator = new EntraIdAuthenticator(["https://trino.example.com/.default"], credential);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/statement");
        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "entra-token-1"), request.Headers.Authorization);
        Assert.Equal(1, credential.CallCount);
        Assert.Equal(["https://trino.example.com/.default"], credential.RequestedScopes[0]);
    }

    [Fact]
    public async Task ApplyAsync_CachesTokenUntilNearExpiry()
    {
        var credential = FakeTokenCredential.AlwaysReturning("entra-token-1", TimeSpan.FromHours(1));
        using var authenticator = new EntraIdAuthenticator(["scope"], credential);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request1, CancellationToken.None);
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request2, CancellationToken.None);

        Assert.Equal(1, credential.CallCount);
    }

    [Fact]
    public async Task ApplyAsync_RefreshesProactivelyWithinSkew()
    {
        var credential = FakeTokenCredential.AlwaysReturning("entra-token-1", TimeSpan.FromSeconds(30));
        using var authenticator = new EntraIdAuthenticator(["scope"], credential);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request1, CancellationToken.None);
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request2, CancellationToken.None);

        Assert.Equal(2, credential.CallCount);
    }

    [Fact]
    public async Task TryRefreshAsync_OnUnauthorized_ForcesRefresh()
    {
        var credential = FakeTokenCredential.AlwaysReturning("entra-token-1", TimeSpan.FromHours(1));
        using var authenticator = new EntraIdAuthenticator(["scope"], credential);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request, CancellationToken.None);
        Assert.Equal(1, credential.CallCount);

        using var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var refreshed = await authenticator.TryRefreshAsync(unauthorized, CancellationToken.None);

        Assert.True(refreshed);
        Assert.Equal(2, credential.CallCount);
    }

    [Fact]
    public async Task ApplyAsync_ConcurrentCallsFromCold_SingleFlightsRefresh()
    {
        // No token cached yet, so every concurrent caller sees IsNearExpiry() == true; the returned
        // validity comfortably exceeds the default 60s skew, so only the winner of the race should
        // actually acquire a token — everyone else must observe the now-cached token instead.
        var credential = FakeTokenCredential.AlwaysReturning("entra-token-1", TimeSpan.FromHours(1));
        using var authenticator = new EntraIdAuthenticator(["scope"], credential);

        var tasks = Enumerable.Range(0, 16).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
            await authenticator.ApplyAsync(request, CancellationToken.None);
        });
        await Task.WhenAll(tasks);

        Assert.Equal(1, credential.CallCount);
    }
}
