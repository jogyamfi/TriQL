using TriQL.Client.Auth;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Tests;

public sealed class LdapAuthenticatorTests
{
    [Fact]
    public async Task ApplyAsync_Throws_WhenConnectionIsPlaintextHttp()
    {
        var authenticator = new LdapAuthenticator("user", "password");
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://trino.example.com/v1/info");

        await Assert.ThrowsAsync<TrinoConfigurationException>(() => authenticator.ApplyAsync(request, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ApplyAsync_SetsBasicCredential_OverHttps()
    {
        var authenticator = new LdapAuthenticator("user", "password");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");

        await authenticator.ApplyAsync(request, CancellationToken.None);

        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public void Username_ReturnsConfiguredUsername()
    {
        var authenticator = new LdapAuthenticator("directory-user", "password");

        Assert.Equal("directory-user", authenticator.Username);
    }
}
