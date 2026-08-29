using System.Runtime.CompilerServices;
using TriQL.Client.Exceptions;
using TriQL.Data.ADO.Internal;

namespace TriQL.Client.Auth;

/// <summary>
/// Registers <see cref="OAuth2ClientCredentialsAuthenticator"/> and <see cref="EntraIdAuthenticator"/>
/// with the connection-string authenticator registry as soon as this assembly is loaded, making
/// <c>Auth=oauth2-client-credentials</c> and <c>Auth=entra-id</c> resolvable (FR-2.3.4, P6-T3).
/// </summary>
internal static class Registration
{
    // Not application code, but the documented plugin-registration pattern for this extension
    // point (see AuthenticatorRegistry's own doc comment) — CA2255 flags all module initializers
    // outside app code by design; this one is deliberate.
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void Register()
#pragma warning restore CA2255
    {
        AuthenticatorRegistry.Register("oauth2-client-credentials", CreateOAuth2);
        AuthenticatorRegistry.Register("entra-id", CreateEntraId);
    }

    private static ITrinoAuthenticator CreateOAuth2(AuthenticatorContext context)
    {
        var endpoint = context.TokenEndpoint is { Length: > 0 } value
            ? new Uri(value)
            : throw new TrinoConfigurationException("Auth=oauth2-client-credentials requires the connection string 'TokenEndpoint' key.");

        var clientId = context.ClientId is { Length: > 0 } id
            ? id
            : throw new TrinoConfigurationException("Auth=oauth2-client-credentials requires the connection string 'ClientId' key.");

        var clientSecret = context.ClientSecret
            ?? throw new TrinoConfigurationException("Auth=oauth2-client-credentials requires the connection string 'ClientSecret' key.");

        return new OAuth2ClientCredentialsAuthenticator(endpoint, clientId, clientSecret, SplitScopes(context.Scopes));
    }

    private static ITrinoAuthenticator CreateEntraId(AuthenticatorContext context)
    {
        var scopes = SplitScopes(context.Scopes);
        if (scopes.Length == 0)
        {
            throw new TrinoConfigurationException("Auth=entra-id requires the connection string 'Scopes' key.");
        }

        Azure.Core.TokenCredential? credential = null;
        if (context is { TenantId.Length: > 0, ClientId.Length: > 0, ClientSecret.Length: > 0 })
        {
            credential = new Azure.Identity.ClientSecretCredential(context.TenantId, context.ClientId, context.ClientSecret);
        }

        return new EntraIdAuthenticator(scopes, credential);
    }

    private static string[] SplitScopes(string? scopes) =>
        scopes is { Length: > 0 }
            ? scopes.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
