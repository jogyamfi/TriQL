using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Data.ADO.Internal;

namespace TriQL.Client.Auth.Tests;

/// <summary>P6-T3: entra-id/oauth2-client-credentials must resolve via the connection-string registry.</summary>
public sealed class AuthenticatorRegistrationTests
{
    [Fact]
    public void Resolve_OAuth2ClientCredentials_ReturnsConfiguredAuthenticator()
    {
        var context = new AuthenticatorContext(
            User: null, Password: null, AccessToken: null,
            ClientId: "client-id", ClientSecret: "secret",
            TokenEndpoint: "https://idp.example.com/token", Scopes: "scope1 scope2",
            TenantId: null, ClientCertificatePath: null, ClientCertificateThumbprint: null);

        using var authenticator = (OAuth2ClientCredentialsAuthenticator)AuthenticatorRegistry.Resolve("oauth2-client-credentials", context);

        Assert.NotNull(authenticator);
    }

    [Fact]
    public void Resolve_OAuth2ClientCredentials_MissingTokenEndpoint_Throws()
    {
        var context = new AuthenticatorContext(
            User: null, Password: null, AccessToken: null,
            ClientId: "client-id", ClientSecret: "secret",
            TokenEndpoint: null, Scopes: null,
            TenantId: null, ClientCertificatePath: null, ClientCertificateThumbprint: null);

        Assert.Throws<TrinoConfigurationException>(() => AuthenticatorRegistry.Resolve("oauth2-client-credentials", context));
    }

    [Fact]
    public void Resolve_EntraId_ReturnsConfiguredAuthenticator()
    {
        var context = new AuthenticatorContext(
            User: null, Password: null, AccessToken: null,
            ClientId: "client-id", ClientSecret: "secret",
            TokenEndpoint: null, Scopes: "https://trino.example.com/.default",
            TenantId: "tenant-id", ClientCertificatePath: null, ClientCertificateThumbprint: null);

        using var authenticator = (EntraIdAuthenticator)AuthenticatorRegistry.Resolve("entra-id", context);

        Assert.NotNull(authenticator);
    }

    [Fact]
    public void Resolve_EntraId_MissingScopes_Throws()
    {
        var context = new AuthenticatorContext(
            User: null, Password: null, AccessToken: null,
            ClientId: null, ClientSecret: null,
            TokenEndpoint: null, Scopes: null,
            TenantId: null, ClientCertificatePath: null, ClientCertificateThumbprint: null);

        Assert.Throws<TrinoConfigurationException>(() => AuthenticatorRegistry.Resolve("entra-id", context));
    }
}
