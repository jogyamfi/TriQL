using TriQL.Client.Exceptions;

namespace TriQL.Client.Auth;

/// <summary>
/// Behaves as <see cref="BasicAuthenticator"/> but unconditionally refuses to operate over a
/// non-TLS connection, since Trino requires LDAP authentication over HTTPS. There is no override
/// for this refusal, unlike the general plaintext-credential guard (FR-1.1.3). See FR-2.2.3.
/// </summary>
public sealed class LdapAuthenticator : BasicAuthenticator
{
    /// <summary>Initializes a new instance of the <see cref="LdapAuthenticator"/> class.</summary>
    public LdapAuthenticator(string username, string password)
        : base(username, password)
    {
    }

    /// <inheritdoc/>
    public override ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.RequestUri?.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new TrinoConfigurationException(
                "LdapAuthenticator requires an https connection; Trino does not support LDAP authentication over plaintext http.");
        }

        return base.ApplyAsync(request, cancellationToken);
    }
}
