using System.Net.Http.Headers;
using System.Text;

namespace TriQL.Client.Internal;

/// <summary>
/// Redacts credential-bearing header values before they reach logs, exception messages, or trace tags (SEC-1).
/// </summary>
internal static class Redactor
{
    public const string RedactedPlaceholder = "***REDACTED***";

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "X-Trino-Extra-Credential",
        "Cookie",
        "Set-Cookie",
    };

    public static bool IsSensitiveHeader(string headerName) => SensitiveHeaderNames.Contains(headerName);

    public static string RedactHeaderValue(string headerName, string value) =>
        IsSensitiveHeader(headerName) ? RedactedPlaceholder : value;

    /// <summary>Renders a redacted, single-line dump of request/response headers suitable for Trace logging.</summary>
    public static string DumpHeaders(HttpHeaders headers)
    {
        var builder = new StringBuilder();
        foreach (var header in headers)
        {
            var value = RedactHeaderValue(header.Key, string.Join(",", header.Value));
            builder.Append(header.Key).Append('=').Append(value).Append("; ");
        }

        return builder.ToString();
    }
}
