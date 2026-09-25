using System.Globalization;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Internal;

/// <summary>
/// SSRF containment for server-supplied segment and acknowledgement URIs (SEC-7, P5-T9, the closed
/// G3 decision in requirements.md §23 Q6). Mirrors <see cref="RedirectHandler"/>'s scheme-downgrade
/// pattern: every check is scoped to one specific failure mode rather than a blanket accept/reject.
/// </summary>
internal static class UriGuard
{
    /// <summary>
    /// Validates a segment <c>ackUri</c>: <c>https</c> is required unconditionally, and the host
    /// MUST match the coordinator's origin exactly (scheme, host, and port) — G3 restricts
    /// acknowledgement to the session origin even though segment payload URIs may legitimately be
    /// off-origin object storage.
    /// </summary>
    public static void ValidateAckUri(Uri ackUri, Uri coordinatorOrigin)
    {
        RequireHttps(ackUri, "ackUri");

        if (!IsSameOrigin(ackUri, coordinatorOrigin))
        {
            throw new TrinoProtocolException(
                $"The server-supplied ackUri '{ackUri}' does not share the coordinator's origin " +
                $"({OriginOf(coordinatorOrigin)}). Segment acknowledgement is restricted to the session origin (SEC-7).");
        }
    }

    /// <summary>
    /// Validates a segment <c>uri</c>: <c>https</c> is required unconditionally; the host is
    /// permitted off-origin (object storage is legitimately elsewhere) unless
    /// <paramref name="hostAllowlist"/> is non-empty, in which case the host MUST appear in it.
    /// </summary>
    public static void ValidateSegmentUri(Uri segmentUri, IReadOnlyList<string> hostAllowlist)
    {
        RequireHttps(segmentUri, "segment uri");

        if (hostAllowlist.Count > 0 && !Contains(hostAllowlist, segmentUri.Host))
        {
            throw new TrinoProtocolException(
                $"The server-supplied segment uri host '{segmentUri.Host}' is not present in the configured " +
                "TrinoSessionOptions.SegmentHostAllowlist.");
        }
    }

    /// <summary>Whether <paramref name="uri"/> shares <paramref name="origin"/>'s scheme, host, and port exactly.</summary>
    public static bool IsSameOrigin(Uri uri, Uri origin) =>
        string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == origin.Port;

    private static bool Contains(IReadOnlyList<string> hostAllowlist, string host)
    {
        foreach (var allowed in hostAllowlist)
        {
            if (string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Segment and ack endpoints are required to use https unconditionally — stricter than the
    // general nextUri downgrade-only rule — because they carry either result data or the
    // credential/headers needed to fetch it, and may legitimately traverse the public internet to
    // reach object storage.
    private static void RequireHttps(Uri uri, string what)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new TrinoProtocolException(
                $"The server-supplied {what} '{uri}' does not use the 'https' scheme. Spooled segment and " +
                "acknowledgement endpoints are required to use https regardless of the coordinator's own scheme (SEC-7).");
        }
    }

    private static string OriginOf(Uri uri) =>
        string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://{uri.Host}:{uri.Port}");
}
