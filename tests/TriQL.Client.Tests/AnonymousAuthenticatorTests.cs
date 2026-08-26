using TriQL.Client.Auth;

namespace TriQL.Client.Tests;

public sealed class AnonymousAuthenticatorTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotSetAuthorizationHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        await AnonymousAuthenticator.Instance.ApplyAsync(request, CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task TryRefreshAsync_AlwaysReturnsFalse()
    {
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        var refreshed = await AnonymousAuthenticator.Instance.TryRefreshAsync(response, CancellationToken.None);

        Assert.False(refreshed);
    }

    [Fact]
    public void Instance_IsASingleton()
    {
        Assert.Same(AnonymousAuthenticator.Instance, AnonymousAuthenticator.Instance);
    }

    [Fact]
    public void ConfigureHandler_DoesNotThrow()
    {
        using var handler = new SocketsHttpHandler();

        AnonymousAuthenticator.Instance.ConfigureHandler(handler);
    }

    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        await AnonymousAuthenticator.Instance.InitializeAsync(CancellationToken.None);
    }
}
