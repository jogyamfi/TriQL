using System.Globalization;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;

namespace TriQL.Client;

/// <summary>
/// Mutable configuration for a <see cref="TrinoClient"/>: server, credentials, catalog, schema,
/// session properties, TLS, and buffering. See FR-1.1.
/// </summary>
public sealed class TrinoSessionOptions
{
    /// <summary>The coordinator base URI, e.g. <c>https://trino.example.com:443/</c>. Required.</summary>
    public Uri? Server { get; set; }

    /// <summary>The effective Trino user. Maps to <c>X-Trino-User</c>. Defaults to <see cref="Environment.UserName"/>.</summary>
    public string? User { get; set; } = Environment.UserName;

    /// <summary>The user to run the query as, distinct from the authenticated <see cref="User"/>. Maps to <c>X-Trino-Authorization-User</c>.</summary>
    public string? AuthorizationUser { get; set; }

    /// <summary>An informational principal identifier. Not sent to the server.</summary>
    public string? Principal { get; set; }

    /// <summary>The client source identifier. Maps to <c>X-Trino-Source</c>.</summary>
    public string Source { get; set; } = "triql-dotnet";

    /// <summary>The initial catalog. Maps to <c>X-Trino-Catalog</c>.</summary>
    public string? Catalog { get; set; }

    /// <summary>The initial schema. Maps to <c>X-Trino-Schema</c>.</summary>
    public string? Schema { get; set; }

    /// <summary>The initial SQL path. Maps to <c>X-Trino-Path</c>.</summary>
    public string? Path { get; set; }

    /// <summary>The session time zone. Maps to <c>X-Trino-Time-Zone</c>. Defaults to the host's local zone id.</summary>
    public string? TimeZone { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>The session locale. Maps to <c>X-Trino-Language</c>. Defaults to the current culture.</summary>
    public string? Locale { get; set; } = CultureInfo.CurrentCulture.Name;

    /// <summary>A caller-supplied trace token. Maps to <c>X-Trino-Trace-Token</c>.</summary>
    public string? TraceToken { get; set; }

    /// <summary>Free-form client information. Maps to <c>X-Trino-Client-Info</c>.</summary>
    public string? ClientInfo { get; set; }

    /// <summary>Client tags. Maps to <c>X-Trino-Client-Tags</c>.</summary>
    public ISet<string> ClientTags { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Initial session properties. Maps to <c>X-Trino-Session</c>.</summary>
    public IDictionary<string, string> SessionProperties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Initial prepared statements, keyed by name. Maps to <c>X-Trino-Prepared-Statement</c>.</summary>
    public IDictionary<string, string> PreparedStatements { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Resource estimates for the query. Maps to <c>X-Trino-Resource-Estimate</c>.</summary>
    public IDictionary<string, string> ResourceEstimates { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Extra credentials passed through to connectors. Maps to <c>X-Trino-Extra-Credential</c>. Treated as secret (SEC-1).</summary>
    public IDictionary<string, string> ExtraCredentials { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Initial role selections, keyed by catalog (empty key for the system role). Maps to <c>X-Trino-Role</c>.</summary>
    public IDictionary<string, TrinoSelectedRole> Roles { get; } = new Dictionary<string, TrinoSelectedRole>(StringComparer.Ordinal);

    /// <summary>Additional headers sent verbatim on every request.</summary>
    public IDictionary<string, string> AdditionalHeaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The authenticator used to credential requests. Defaults to <see cref="AnonymousAuthenticator"/>.</summary>
    public ITrinoAuthenticator? Authenticator { get; set; }

    /// <summary>The client-side query deadline. <see langword="null"/> means unbounded.</summary>
    public TimeSpan? QueryTimeout { get; set; }

    /// <summary>The per-HTTP-request timeout. Default 100 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>The read-ahead buffer byte budget. Default 50 MB. See FR-6.</summary>
    public long ReadAheadBufferBytes { get; set; } = 52_428_800;

    /// <summary>The <c>targetResultSize</c> query parameter value, in bytes. Default 5 MB.</summary>
    public long TargetResultSizeBytes { get; set; } = 5_242_880;

    /// <summary>The initial adaptive polling delay after an empty page. Default 50 ms. See FR-4.4.5.</summary>
    public TimeSpan PollingBackoffInitialDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>The multiplier applied to the polling delay after each empty page. Default 1.2. See FR-4.4.5.</summary>
    public double PollingBackoffMultiplier { get; set; } = 1.2;

    /// <summary>The maximum adaptive polling delay. Default 5 seconds. See FR-4.4.5.</summary>
    public TimeSpan PollingBackoffMaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Disables automatic response decompression. Default <see langword="false"/>.</summary>
    public bool CompressionDisabled { get; set; }

    /// <summary>
    /// The spooled-protocol encodings to request, in preference order. Empty forces the direct protocol.
    /// Defaults to empty (opt-in) in 1.0 per FR-5.1.6.
    /// </summary>
    public IReadOnlyList<string> QueryDataEncodings { get; set; } = [];

    /// <summary>Issue a <c>/v1/info</c> request on <c>Open()</c> to confirm the server is ready. Default <see langword="false"/>.</summary>
    public bool TestConnectionOnOpen { get; set; }

    /// <summary>TLS policy.</summary>
    public TrinoTlsOptions Tls { get; } = new();

    /// <summary>
    /// Constructs a <see cref="Server"/> URI from its parts, mirroring the connection-string model. See FR-1.1.4.
    /// </summary>
    public static Uri FromParts(string host, int port, bool useTls, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        var builder = new UriBuilder(useTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, host, port, path ?? string.Empty);
        return builder.Uri;
    }

    /// <summary>
    /// Validates the options. Called automatically on first use by <see cref="TrinoClient"/>. See FR-1.1.2, FR-1.1.3.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="Server"/> is missing or not an absolute http/https URI.</exception>
    /// <exception cref="TrinoConfigurationException">
    /// A credential-transmitting authenticator is configured over plaintext <c>http</c> without
    /// <see cref="TrinoTlsOptions.AllowPlaintextCredentials"/>.
    /// </exception>
    public void Validate()
    {
        if (Server is null || !Server.IsAbsoluteUri)
        {
            throw new ArgumentException("Server must be set to an absolute URI.", nameof(Server));
        }

        if (Server.Scheme != Uri.UriSchemeHttp && Server.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"Server scheme must be 'http' or 'https', but was '{Server.Scheme}'.", nameof(Server));
        }

        if (Server.Scheme == Uri.UriSchemeHttp
            && Authenticator is ITransmitsBearerCredential
            && !Tls.AllowPlaintextCredentials)
        {
            throw new TrinoConfigurationException(
                "The configured authenticator transmits a credential and Server uses the 'http' scheme. " +
                "Set Tls.AllowPlaintextCredentials = true to permit this explicitly, or use 'https'.");
        }

        if (ReadAheadBufferBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadAheadBufferBytes), ReadAheadBufferBytes, "ReadAheadBufferBytes must be positive.");
        }

        if (TargetResultSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetResultSizeBytes), TargetResultSizeBytes, "TargetResultSizeBytes must be positive.");
        }

        if (ReadAheadBufferBytes < TargetResultSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadAheadBufferBytes),
                ReadAheadBufferBytes,
                "ReadAheadBufferBytes must be at least TargetResultSizeBytes so at least one maximal page fits.");
        }

        if (PollingBackoffInitialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollingBackoffInitialDelay), PollingBackoffInitialDelay, "PollingBackoffInitialDelay must be positive.");
        }

        if (PollingBackoffMultiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(PollingBackoffMultiplier), PollingBackoffMultiplier, "PollingBackoffMultiplier must be at least 1.0.");
        }

        if (PollingBackoffMaxDelay < PollingBackoffInitialDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(PollingBackoffMaxDelay), PollingBackoffMaxDelay, "PollingBackoffMaxDelay must be at least PollingBackoffInitialDelay.");
        }
    }
}
