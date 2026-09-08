using System.Data.Common;
using System.Globalization;
using TriQL.Client;
using TriQL.Data.ADO.Internal;

namespace TriQL.Data.ADO;

/// <summary>
/// A strongly typed <see cref="DbConnectionStringBuilder"/> for every key in the connection-string
/// key table (Appendix B). See FR-1.3.
/// </summary>
#pragma warning disable CA1010 // The required DbConnectionStringBuilder base only implements non-generic ICollection; there is no generic ADO.NET contract to satisfy instead.
public sealed class TrinoConnectionStringBuilder : DbConnectionStringBuilder
#pragma warning restore CA1010
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Port", "EnableSsl", "Path", "Server", "User", "Password", "AuthorizationUser", "Auth",
        "AccessToken", "ClientId", "ClientSecret", "TokenEndpoint", "Scopes", "TenantId", "Catalog", "Schema",
        "Source", "ClientInfo", "ClientTags", "TraceToken", "TimeZone", "Locale", "SessionProperties",
        "ExtraCredentials", "ResourceEstimates", "Roles", "QueryTimeout", "RequestTimeout",
        "ReadAheadBufferBytes", "TargetResultSizeBytes", "QueryDataEncoding", "CompressionDisabled",
        "TestConnection", "AllowSelfSignedCertificate", "AllowHostNameMismatch", "UseSystemTrustStore",
        "TrustedCertificatePath", "ClientCertificatePath", "ClientCertificateThumbprint", "AllowPlaintextCredentials",
    };

    private static readonly string[] SecretKeys = ["Password", "AccessToken", "ClientSecret"];

    /// <summary>Initializes a new, empty instance.</summary>
    public TrinoConnectionStringBuilder()
    {
    }

    /// <summary>Initializes a new instance by parsing <paramref name="connectionString"/>.</summary>
    public TrinoConnectionStringBuilder(string connectionString) => ConnectionString = connectionString;

    /// <summary>
    /// When <see langword="true"/>, <see cref="ToString"/> redacts <see cref="Password"/>,
    /// <see cref="AccessToken"/>, and <see cref="ClientSecret"/>. The default <see langword="false"/>
    /// preserves round-trip fidelity (FR-1.3.4); callers are responsible for not logging the raw string.
    /// </summary>
    public bool RedactSecrets { get; set; }

    /// <summary>The coordinator host name. Combined with <see cref="Port"/>/<see cref="EnableSsl"/>/<see cref="Path"/> unless <see cref="Server"/> is set.</summary>
    public string? Host { get => GetString("Host"); set => SetValue("Host", value); }

    /// <summary>The coordinator port. Defaults to <c>443</c> when <see cref="EnableSsl"/>, else <c>8080</c>.</summary>
    public int? Port { get => GetInt32("Port"); set => SetValue("Port", value); }

    /// <summary>Whether to use TLS. Default <see langword="true"/>.</summary>
    public bool EnableSsl { get => GetBoolean("EnableSsl") ?? true; set => SetValue("EnableSsl", value); }

    /// <summary>The coordinator URI path.</summary>
    public string? Path { get => GetString("Path"); set => SetValue("Path", value); }

    /// <summary>The full coordinator URI. Mutually exclusive with <see cref="Host"/>/<see cref="Port"/>/<see cref="EnableSsl"/>.</summary>
    public string? Server { get => GetString("Server"); set => SetValue("Server", value); }

    /// <summary>The Trino user. Defaults to <see cref="Environment.UserName"/>.</summary>
    public string? User { get => GetString("User"); set => SetValue("User", value); }

    /// <summary>The Basic/LDAP password. Treated as a secret (SEC-1).</summary>
    public string? Password { get => GetString("Password"); set => SetValue("Password", value); }

    /// <summary>The user to run the query as, distinct from the authenticated <see cref="User"/>.</summary>
    public string? AuthorizationUser { get => GetString("AuthorizationUser"); set => SetValue("AuthorizationUser", value); }

    /// <summary>
    /// The authenticator short name: <c>none</c>, <c>basic</c>, <c>ldap</c>, <c>jwt</c>,
    /// <c>oauth2-client-credentials</c>, <c>entra-id</c>, or <c>certificate</c> (FR-1.3.5).
    /// </summary>
    public string? Auth { get => GetString("Auth"); set => SetValue("Auth", value); }

    /// <summary>The JWT bearer token. Treated as a secret (SEC-1).</summary>
    public string? AccessToken { get => GetString("AccessToken"); set => SetValue("AccessToken", value); }

    /// <summary>The OAuth2/Entra ID client id.</summary>
    public string? ClientId { get => GetString("ClientId"); set => SetValue("ClientId", value); }

    /// <summary>The OAuth2 client secret. Treated as a secret (SEC-1).</summary>
    public string? ClientSecret { get => GetString("ClientSecret"); set => SetValue("ClientSecret", value); }

    /// <summary>The OAuth2 token endpoint.</summary>
    public string? TokenEndpoint { get => GetString("TokenEndpoint"); set => SetValue("TokenEndpoint", value); }

    /// <summary>Comma-separated OAuth2 scopes.</summary>
    public string? Scopes { get => GetString("Scopes"); set => SetValue("Scopes", value); }

    /// <summary>The Entra ID tenant.</summary>
    public string? TenantId { get => GetString("TenantId"); set => SetValue("TenantId", value); }

    /// <summary>The initial catalog.</summary>
    public string? Catalog { get => GetString("Catalog"); set => SetValue("Catalog", value); }

    /// <summary>The initial schema.</summary>
    public string? Schema { get => GetString("Schema"); set => SetValue("Schema", value); }

    /// <summary>The client source identifier. Default <c>triql-dotnet</c>.</summary>
    public string? Source { get => GetString("Source"); set => SetValue("Source", value); }

    /// <summary>Free-form client information.</summary>
    public string? ClientInfo { get => GetString("ClientInfo"); set => SetValue("ClientInfo", value); }

    /// <summary>Comma-separated client tags.</summary>
    public string? ClientTags { get => GetString("ClientTags"); set => SetValue("ClientTags", value); }

    /// <summary>A caller-supplied trace token.</summary>
    public string? TraceToken { get => GetString("TraceToken"); set => SetValue("TraceToken", value); }

    /// <summary>The session time zone. Defaults to the host zone.</summary>
    public string? TimeZone { get => GetString("TimeZone"); set => SetValue("TimeZone", value); }

    /// <summary>The session locale. Defaults to the current culture.</summary>
    public string? Locale { get => GetString("Locale"); set => SetValue("Locale", value); }

    /// <summary>Comma-separated <c>k=v</c> initial session properties.</summary>
    public string? SessionProperties { get => GetString("SessionProperties"); set => SetValue("SessionProperties", value); }

    /// <summary>Comma-separated <c>k=v</c> extra credentials. Treated as secret (SEC-1).</summary>
    public string? ExtraCredentials { get => GetString("ExtraCredentials"); set => SetValue("ExtraCredentials", value); }

    /// <summary>Comma-separated <c>k=v</c> resource estimates.</summary>
    public string? ResourceEstimates { get => GetString("ResourceEstimates"); set => SetValue("ResourceEstimates", value); }

    /// <summary>Comma-separated <c>catalog=role</c> initial role selections.</summary>
    public string? Roles { get => GetString("Roles"); set => SetValue("Roles", value); }

    /// <summary>The client-side query deadline, in seconds. <c>0</c> (the default) means unbounded.</summary>
    public int? QueryTimeout { get => GetInt32("QueryTimeout"); set => SetValue("QueryTimeout", value); }

    /// <summary>The per-HTTP-request timeout, in seconds. Default <c>100</c>.</summary>
    public int? RequestTimeout { get => GetInt32("RequestTimeout"); set => SetValue("RequestTimeout", value); }

    /// <summary>The read-ahead buffer byte budget. Default <c>52428800</c>.</summary>
    public long? ReadAheadBufferBytes { get => GetInt64("ReadAheadBufferBytes"); set => SetValue("ReadAheadBufferBytes", value); }

    /// <summary>The <c>targetResultSize</c> query parameter value, in bytes. Default <c>5242880</c>.</summary>
    public long? TargetResultSizeBytes { get => GetInt64("TargetResultSizeBytes"); set => SetValue("TargetResultSizeBytes", value); }

    /// <summary>Comma-separated spooled-protocol encodings, in preference order. Empty forces the direct protocol.</summary>
    public string? QueryDataEncoding { get => GetString("QueryDataEncoding"); set => SetValue("QueryDataEncoding", value); }

    /// <summary>Disables automatic response decompression. Default <see langword="false"/>.</summary>
    public bool CompressionDisabled { get => GetBoolean("CompressionDisabled") ?? false; set => SetValue("CompressionDisabled", value); }

    /// <summary>Issues a <c>/v1/info</c> request on <c>Open()</c> to confirm the server is ready. Default <see langword="false"/>.</summary>
    public bool TestConnection { get => GetBoolean("TestConnection") ?? false; set => SetValue("TestConnection", value); }

    /// <summary>Accept a certificate chain whose only failure is an untrusted (self-signed) root. Default <see langword="false"/>.</summary>
    public bool AllowSelfSignedCertificate { get => GetBoolean("AllowSelfSignedCertificate") ?? false; set => SetValue("AllowSelfSignedCertificate", value); }

    /// <summary>Accept a certificate whose only failure is a host name mismatch. Default <see langword="false"/>.</summary>
    public bool AllowHostNameMismatch { get => GetBoolean("AllowHostNameMismatch") ?? false; set => SetValue("AllowHostNameMismatch", value); }

    /// <summary>Validate the server certificate against the OS trust store. Default <see langword="true"/>.</summary>
    public bool UseSystemTrustStore { get => GetBoolean("UseSystemTrustStore") ?? true; set => SetValue("UseSystemTrustStore", value); }

    /// <summary>A PEM or DER file containing an additional trusted root certificate.</summary>
    public string? TrustedCertificatePath { get => GetString("TrustedCertificatePath"); set => SetValue("TrustedCertificatePath", value); }

    /// <summary>A PFX/PEM file containing the client certificate for mutual TLS.</summary>
    public string? ClientCertificatePath { get => GetString("ClientCertificatePath"); set => SetValue("ClientCertificatePath", value); }

    /// <summary>An <see cref="System.Security.Cryptography.X509Certificates.X509Store"/> thumbprint identifying the client certificate.</summary>
    public string? ClientCertificateThumbprint { get => GetString("ClientCertificateThumbprint"); set => SetValue("ClientCertificateThumbprint", value); }

    /// <summary>Permit transmission of a bearer credential over an unencrypted <c>http</c> connection. Default <see langword="false"/>.</summary>
    public bool AllowPlaintextCredentials { get => GetBoolean("AllowPlaintextCredentials") ?? false; set => SetValue("AllowPlaintextCredentials", value); }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="keyword"/> is not a recognized key (FR-1.3.3).</exception>
    public override object this[string keyword]
    {
        get => base[keyword];
#pragma warning disable CS8765 // DbConnectionStringBuilder's setter accepts null (removing the key); the nullable annotation on the base member does not surface cleanly through an indexer override.
        set
#pragma warning restore CS8765
        {
            ArgumentException.ThrowIfNullOrEmpty(keyword);
            if (!KnownKeys.Contains(keyword))
            {
                throw new ArgumentException(
                    $"Unrecognized connection string key '{keyword}'. Known keys: {string.Join(", ", KnownKeys.Order(StringComparer.OrdinalIgnoreCase))}.",
                    nameof(keyword));
            }

            if (value is null)
            {
                Remove(keyword);
                return;
            }

            base[keyword] = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Round-trips through the base <see cref="DbConnectionStringBuilder"/> quoting rules
    /// (FR-1.3.4). When <see cref="RedactSecrets"/> is set, <see cref="Password"/>,
    /// <see cref="AccessToken"/>, and <see cref="ClientSecret"/> are replaced with <c>***</c>.
    /// </remarks>
    public override string ToString()
    {
        if (!RedactSecrets)
        {
            return base.ToString();
        }

        var redacted = new DbConnectionStringBuilder { ConnectionString = base.ToString() };
        foreach (var secretKey in SecretKeys)
        {
            if (redacted.ContainsKey(secretKey))
            {
                redacted[secretKey] = "***";
            }
        }

        return redacted.ToString();
    }

    /// <summary>Builds the equivalent <see cref="TrinoSessionOptions"/>, resolving <see cref="Auth"/> via <see cref="AuthenticatorRegistry"/>.</summary>
    internal TrinoSessionOptions ToSessionOptions()
    {
        var options = new TrinoSessionOptions
        {
            Server = ResolveServerUri(),
            User = User ?? Environment.UserName,
            AuthorizationUser = AuthorizationUser,
            Catalog = Catalog,
            Schema = Schema,
            Path = Path,
            Source = Source ?? "triql-dotnet",
            ClientInfo = ClientInfo,
            TraceToken = TraceToken,
            Locale = Locale ?? CultureInfo.CurrentCulture.Name,
            QueryTimeout = QueryTimeout is > 0 ? TimeSpan.FromSeconds(QueryTimeout.Value) : null,
            RequestTimeout = TimeSpan.FromSeconds(RequestTimeout ?? 100),
            ReadAheadBufferBytes = ReadAheadBufferBytes ?? 52_428_800,
            TargetResultSizeBytes = TargetResultSizeBytes ?? 5_242_880,
            CompressionDisabled = CompressionDisabled,
            TestConnectionOnOpen = TestConnection,
        };

        if (TimeZone is not null)
        {
            options.TimeZone = TimeZone;
        }

        if (ClientTags is { Length: > 0 } clientTags)
        {
            foreach (var tag in Split(clientTags))
            {
                options.ClientTags.Add(tag);
            }
        }

        AddPairs(SessionProperties, options.SessionProperties);
        AddPairs(ExtraCredentials, options.ExtraCredentials);
        AddPairs(ResourceEstimates, options.ResourceEstimates);

        if (Roles is { Length: > 0 } roles)
        {
            foreach (var pair in Split(roles))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    options.Roles[parts[0]] = TrinoSelectedRole.Named(parts[1]);
                }
            }
        }

        if (QueryDataEncoding is { Length: > 0 } encodings)
        {
            options.QueryDataEncodings = Split(encodings);
        }

        options.Tls.AllowSelfSignedCertificate = AllowSelfSignedCertificate;
        options.Tls.AllowHostNameMismatch = AllowHostNameMismatch;
        options.Tls.UseSystemTrustStore = UseSystemTrustStore;
        options.Tls.TrustedRootCertificatePath = TrustedCertificatePath;
        options.Tls.AllowPlaintextCredentials = AllowPlaintextCredentials;

        var context = new AuthenticatorContext(
            options.User, Password, AccessToken, ClientId, ClientSecret, TokenEndpoint, Scopes, TenantId,
            ClientCertificatePath, ClientCertificateThumbprint);
        options.Authenticator = AuthenticatorRegistry.Resolve(Auth ?? "none", context);

        return options;
    }

    private Uri? ResolveServerUri()
    {
        if (Server is { Length: > 0 } server)
        {
            return new Uri(server, UriKind.Absolute);
        }

        if (Host is { Length: > 0 } host)
        {
            var enableSsl = EnableSsl;
            var port = Port ?? (enableSsl ? 443 : 8080);
            return TrinoSessionOptions.FromParts(host, port, enableSsl, Path);
        }

        return null;
    }

    private static string[] Split(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static void AddPairs(string? value, IDictionary<string, string> target)
    {
        if (value is not { Length: > 0 })
        {
            return;
        }

        foreach (var pair in Split(value))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                target[parts[0]] = parts[1];
            }
        }
    }

    private string? GetString(string key) =>
        TryGetValue(key, out var value) && value is not null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    private int? GetInt32(string key) =>
        TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;

    private long? GetInt64(string key) =>
        TryGetValue(key, out var value) && value is not null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    private bool? GetBoolean(string key) =>
        TryGetValue(key, out var value) && value is not null ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : null;

    private void SetValue(string key, object? value)
    {
        if (value is null)
        {
            Remove(key);
        }
        else
        {
            this[key] = value;
        }
    }
}
