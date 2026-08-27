using TriQL.Client.Auth;
using TriQL.Client.Exceptions;

namespace TriQL.Data.ADO.Internal;

/// <summary>
/// The credential material a connection string may carry for authenticator resolution. See FR-1.3.5.
/// </summary>
internal readonly record struct AuthenticatorContext(
    string? User,
    string? Password,
    string? AccessToken,
    string? ClientId,
    string? ClientSecret,
    string? TokenEndpoint,
    string? Scopes,
    string? TenantId,
    string? ClientCertificatePath,
    string? ClientCertificateThumbprint);

/// <summary>
/// Resolves the connection-string <c>Auth</c> key to an <see cref="ITrinoAuthenticator"/> through a
/// static registry, never through reflective type loading (FR-1.3.5, NFR-COMPAT-3, R3, P4-T2).
/// Authenticators requiring <c>TriQL.Client.Auth</c> (<c>entra-id</c>, <c>oauth2-client-credentials</c>)
/// are not registered here, since that package is not referenced by <c>TriQL.Data.ADO</c> (FR-2.3.3);
/// <see cref="Register"/> is the extension point that package uses to add itself when referenced.
/// </summary>
internal static class AuthenticatorRegistry
{
    private static readonly object Gate = new();

    private static readonly Dictionary<string, Func<AuthenticatorContext, ITrinoAuthenticator>> Factories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["none"] = _ => AnonymousAuthenticator.Instance,
            ["basic"] = context => new BasicAuthenticator(RequireUser(context), RequirePassword(context)),
            ["ldap"] = context => new LdapAuthenticator(RequireUser(context), RequirePassword(context)),
            ["jwt"] = context => new JwtAuthenticator(RequireAccessToken(context)),
            ["certificate"] = ResolveCertificateAuthenticator,
        };

    private static readonly IReadOnlyDictionary<string, string> PackageHints =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["entra-id"] = "TriQL.Client.Auth",
            ["oauth2-client-credentials"] = "TriQL.Client.Auth",
        };

    /// <summary>
    /// Registers (or replaces) the factory for an authenticator short name. The extension point
    /// used by packages such as <c>TriQL.Client.Auth</c> to make their providers reachable from a
    /// connection string (FR-2.3.4).
    /// </summary>
    public static void Register(string name, Func<AuthenticatorContext, ITrinoAuthenticator> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            Factories[name] = factory;
        }
    }

    /// <summary>Resolves <paramref name="authKind"/> to an authenticator, or throws naming the missing package.</summary>
    public static ITrinoAuthenticator Resolve(string authKind, in AuthenticatorContext context)
    {
        ArgumentException.ThrowIfNullOrEmpty(authKind);

        Func<AuthenticatorContext, ITrinoAuthenticator>? factory;
        lock (Gate)
        {
            Factories.TryGetValue(authKind, out factory);
        }

        if (factory is not null)
        {
            return factory(context);
        }

        if (PackageHints.TryGetValue(authKind, out var package))
        {
            throw new TrinoConfigurationException(
                $"Authenticator '{authKind}' requires referencing the '{package}' package and letting it register itself; " +
                "it is not implemented in TriQL.Client or TriQL.Data.ADO to keep those packages dependency-free (FR-2.3.3, FR-2.3.4).");
        }

        string[] known;
        lock (Gate)
        {
            known = [.. Factories.Keys, .. PackageHints.Keys];
        }

        throw new ArgumentException(
            $"Unknown authenticator '{authKind}'. Known values: {string.Join(", ", known.Order(StringComparer.OrdinalIgnoreCase))}.",
            nameof(authKind));
    }

    private static ClientCertificateAuthenticator ResolveCertificateAuthenticator(AuthenticatorContext context)
    {
        if (!string.IsNullOrEmpty(context.ClientCertificatePath))
        {
            return ClientCertificateAuthenticator.FromFile(context.ClientCertificatePath);
        }

        if (!string.IsNullOrEmpty(context.ClientCertificateThumbprint))
        {
            return ClientCertificateAuthenticator.FromStoreThumbprint(context.ClientCertificateThumbprint);
        }

        throw new TrinoConfigurationException(
            "Auth=certificate requires ClientCertificatePath or ClientCertificateThumbprint to be set.");
    }

    private static string RequireUser(AuthenticatorContext context) =>
        context.User is { Length: > 0 }
            ? context.User
            : throw new TrinoConfigurationException("This authenticator requires the connection string 'User' key.");

    private static string RequirePassword(AuthenticatorContext context) =>
        context.Password
        ?? throw new TrinoConfigurationException("This authenticator requires the connection string 'Password' key.");

    private static string RequireAccessToken(AuthenticatorContext context) =>
        context.AccessToken is { Length: > 0 }
            ? context.AccessToken
            : throw new TrinoConfigurationException("Auth=jwt requires the connection string 'AccessToken' key.");
}
