using System.Text;
using System.Text.Json;
using TriQL.Client.Auth;

namespace TriQL.Client.Tests;

public sealed class JwtAuthenticatorTests
{
    [Fact]
    public async Task ApplyAsync_SetsBearerToken_ForStaticToken()
    {
        using var authenticator = new JwtAuthenticator("static-token-value");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("static-token-value", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task TryRefreshAsync_WithoutCallback_ReturnsFalse()
    {
        using var authenticator = new JwtAuthenticator("static-token-value");
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        var refreshed = await authenticator.TryRefreshAsync(response, CancellationToken.None);

        Assert.False(refreshed);
    }

    [Fact]
    public async Task TryRefreshAsync_WithCallback_InvokesCallbackAndReturnsTrue()
    {
        var callCount = 0;
        using var authenticator = new JwtAuthenticator(_ =>
        {
            callCount++;
            return ValueTask.FromResult("refreshed-token");
        });
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        var refreshed = await authenticator.TryRefreshAsync(response, CancellationToken.None);

        Assert.True(refreshed);
        Assert.Equal(1, callCount);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");
        await authenticator.ApplyAsync(request, CancellationToken.None);
        Assert.Equal("refreshed-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_ProactivelyRefreshes_WhenCurrentTokenIsWithinSkewOfExpiry()
    {
        var callCount = 0;
        using var authenticator = new JwtAuthenticator(
            _ =>
            {
                callCount++;
                var expiry = callCount == 1 ? DateTimeOffset.UtcNow.AddSeconds(5) : DateTimeOffset.UtcNow.AddHours(1);
                return ValueTask.FromResult(CreateJwtWithExpiry(expiry));
            },
            refreshSkew: TimeSpan.FromSeconds(60));

        // First TryRefreshAsync issues a token that expires in 5s, which is within the 60s skew.
        using var initialResponse = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        await authenticator.TryRefreshAsync(initialResponse, CancellationToken.None);
        Assert.Equal(1, callCount);

        // ApplyAsync must proactively refresh again because the current token is within the skew window.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");
        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ApplyAsync_ProactivelyRefreshes_WhenNoTokenHasEverBeenIssued()
    {
        var callCount = 0;
        using var authenticator = new JwtAuthenticator(_ =>
        {
            callCount++;
            return ValueTask.FromResult(CreateJwtWithExpiry(DateTimeOffset.UtcNow.AddHours(1)));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.NotNull(request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_ForNullOrEmptyStaticToken()
    {
        Assert.Throws<ArgumentException>(() => new JwtAuthenticator(string.Empty));
    }

    private static string CreateJwtWithExpiry(DateTimeOffset expiry)
    {
        var header = Base64UrlEncode("""{"alg":"none"}"""u8.ToArray());
        var payloadJson = JsonSerializer.Serialize(new { exp = expiry.ToUnixTimeSeconds() });
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
