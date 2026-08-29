using System.Net;
using System.Net.Http.Headers;
using TriQL.Client.Auth;

namespace TriQL.Client.Auth.Tests;

/// <summary>Contract tests for P6-T4: acquisition, caching, skew refresh, 401 refresh, single-flight.</summary>
public sealed class OAuth2ClientCredentialsAuthenticatorTests
{
    private static readonly Uri TokenEndpoint = new("https://idp.example.com/oauth2/token");

    [Fact]
    public async Task ApplyAsync_AcquiresAndSendsToken()
    {
        using var handler = StubTokenEndpointHandler.AlwaysReturning("token-1", 3600);
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/statement");
        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "token-1"), request.Headers.Authorization);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("grant_type=client_credentials", handler.CapturedBodies[0]);
        Assert.Contains("client_id=client-id", handler.CapturedBodies[0]);
    }

    [Fact]
    public async Task ApplyAsync_CachesTokenUntilNearExpiry()
    {
        using var handler = StubTokenEndpointHandler.AlwaysReturning("token-1", 3600);
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request1, CancellationToken.None);
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request2, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ApplyAsync_RefreshesProactivelyWithinSkew()
    {
        // expires_in is inside the default 60s skew, so the very next ApplyAsync must refresh.
        using var handler = StubTokenEndpointHandler.AlwaysReturning("token-1", 30);
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request1, CancellationToken.None);
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request2, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TryRefreshAsync_OnUnauthorized_ForcesRefresh()
    {
        using var handler = StubTokenEndpointHandler.AlwaysReturning("token-1", 3600);
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await authenticator.ApplyAsync(request, CancellationToken.None);
        Assert.Equal(1, handler.RequestCount);

        using var unauthorized = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var refreshed = await authenticator.TryRefreshAsync(unauthorized, CancellationToken.None);

        Assert.True(refreshed);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ApplyAsync_ConcurrentCallsFromCold_SingleFlightsRefresh()
    {
        // No token cached yet, so every concurrent caller sees IsNearExpiry() == true; the returned
        // validity comfortably exceeds the default 60s skew, so only the winner of the race should
        // actually hit the token endpoint — everyone else must observe the now-cached token instead.
        using var handler = StubTokenEndpointHandler.AlwaysReturning("token-1", 3600);
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        var tasks = Enumerable.Range(0, 16).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
            await authenticator.ApplyAsync(request, CancellationToken.None);
        });
        await Task.WhenAll(tasks);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RequestTokenAsync_NonSuccessStatus_ThrowsTrinoAuthenticationException()
    {
        using var handler = new StubTokenEndpointHandler(_ => (HttpStatusCode.BadRequest, null, null));
        using var httpClient = new HttpClient(handler);
        using var authenticator = new OAuth2ClientCredentialsAuthenticator(TokenEndpoint, "client-id", "client-secret", httpClient: httpClient);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/");
        await Assert.ThrowsAsync<Exceptions.TrinoAuthenticationException>(() => authenticator.ApplyAsync(request, CancellationToken.None).AsTask());
    }
}
