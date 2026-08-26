using System.Net;

namespace TriQL.Client.Internal;

/// <summary>
/// Writes the Trino protocol request headers (Appendix A.1) from <see cref="TrinoSessionOptions"/>
/// and the live <see cref="TrinoSession"/>. Empty values are omitted entirely (FR-4.1.2).
/// </summary>
internal static class ProtocolHeaders
{
    /// <summary>The client capabilities TriQL declares. See FR-4.1.3.</summary>
    public const string ClientCapabilities = "PATH,PARAMETRIC_DATETIME,SESSION_AUTHORIZATION";

    /// <summary>
    /// Writes the full session header set. Required only on the initial statement submission — <c>nextUri</c>
    /// polls MUST NOT resend these (FR-4.1.5).
    /// </summary>
    public static void WriteSessionHeaders(HttpRequestMessage request, TrinoSessionOptions options, TrinoSession session)
    {
        var user = ResolveUser(options);
        AddHeader(request, "X-Trino-User", user);

        var authorizationUser = session.AuthorizationUser;
        if (!string.IsNullOrEmpty(authorizationUser) && !string.Equals(authorizationUser, user, StringComparison.Ordinal))
        {
            AddHeader(request, "X-Trino-Original-User", user);
        }

        AddHeader(request, "X-Trino-Authorization-User", authorizationUser);
        AddHeader(request, "X-Trino-Source", options.Source);
        AddHeader(request, "X-Trino-Catalog", session.Catalog);
        AddHeader(request, "X-Trino-Schema", session.Schema);
        AddHeader(request, "X-Trino-Path", session.Path);
        AddHeader(request, "X-Trino-Time-Zone", options.TimeZone);
        AddHeader(request, "X-Trino-Language", options.Locale);
        AddHeader(request, "X-Trino-Trace-Token", options.TraceToken);

        AddRepeatedKeyValueHeaders(request, "X-Trino-Session", session.SessionProperties, encodeValue: true);
        AddRoleHeaders(request, session.Roles);
        AddRepeatedKeyValueHeaders(request, "X-Trino-Prepared-Statement", session.PreparedStatements, encodeValue: true);

        AddHeader(request, "X-Trino-Client-Info", options.ClientInfo);
        if (options.ClientTags.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("X-Trino-Client-Tags", string.Join(",", options.ClientTags));
        }

        request.Headers.TryAddWithoutValidation("X-Trino-Client-Capabilities", ClientCapabilities);

        AddRepeatedKeyValueHeaders(request, "X-Trino-Resource-Estimate", options.ResourceEstimates, encodeValue: false);
        AddRepeatedKeyValueHeaders(request, "X-Trino-Extra-Credential", options.ExtraCredentials, encodeValue: false);

        if (options.QueryDataEncodings.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("X-Trino-Query-Data-Encoding", string.Join(",", options.QueryDataEncodings));
        }

        foreach (var (name, value) in options.AdditionalHeaders)
        {
            AddHeader(request, name, value);
        }
    }

    /// <summary>
    /// Resolves the effective <c>X-Trino-User</c>: the explicit <see cref="TrinoSessionOptions.User"/>,
    /// or the <see cref="Auth.BasicAuthenticator.Username"/> when a Basic-family authenticator is configured
    /// and no explicit user was set (FR-2.2.2).
    /// </summary>
    public static string ResolveUser(TrinoSessionOptions options)
    {
        if (!string.IsNullOrEmpty(options.User))
        {
            return options.User;
        }

        if (options.Authenticator is Auth.BasicAuthenticator basic)
        {
            return basic.Username;
        }

        return Environment.UserName;
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static void AddRepeatedKeyValueHeaders(
        HttpRequestMessage request, string headerName, IEnumerable<KeyValuePair<string, string>> values, bool encodeValue)
    {
        foreach (var (key, value) in values)
        {
            var encoded = encodeValue ? WebUtility.UrlEncode(value) : value;
            request.Headers.TryAddWithoutValidation(headerName, $"{key}={encoded}");
        }
    }

    private static void AddRoleHeaders(HttpRequestMessage request, IEnumerable<KeyValuePair<string, TrinoSelectedRole>> roles)
    {
        foreach (var (catalogKey, role) in roles)
        {
            var value = string.IsNullOrEmpty(catalogKey)
                ? role.ToString()
                : $"{catalogKey}={WebUtility.UrlEncode(role.ToString())}";
            request.Headers.TryAddWithoutValidation("X-Trino-Role", value);
        }
    }
}
