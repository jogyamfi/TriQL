using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TriQL.IntegrationTests.Fixtures;

namespace TriQL.IntegrationTests.Spooling;

/// <summary>
/// Issues a statement request directly against a <see cref="SpoolingClusterFixture"/>'s coordinator
/// over HTTP, bypassing <c>TriQL.Client</c> entirely, and collects every spooled segment descriptor
/// across every page. Shared by the Lane B tests that need to inspect the wire-level segment shape
/// itself (P7-T7 SSE-C headers, P7-T8 ack/bucket correlation, P7-T9 origin verification) rather than
/// only the decoded rows the client hands back.
/// </summary>
internal static class RawProtocolProbe
{
    public sealed record Segment(
        string Type,
        Uri? SegmentUri,
        Uri? AckUri,
        IReadOnlyDictionary<string, string[]>? Headers,
        string? InlineDataBase64);

    public static async Task<List<Segment>> CollectSegmentsAsync(
        SpoolingClusterFixture cluster, string sql, string encodings = "json+zstd,json+lz4,json")
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler) { BaseAddress = cluster.ServerUri, Timeout = TimeSpan.FromSeconds(60) };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/statement")
        {
            Content = new StringContent(sql, Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Trino-User", "triql-raw-probe");
        if (encodings.Length > 0)
        {
            request.Headers.Add("X-Trino-Query-Data-Encoding", encodings);
        }

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

        var segments = new List<Segment>();
        var current = body;
        while (true)
        {
            if (current.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.TryGetProperty("message", out var message) ? message.GetString() : "Query failed.");
            }

            if (current.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("segments", out var segmentsJson))
            {
                foreach (var segmentJson in segmentsJson.EnumerateArray())
                {
                    segments.Add(ToSegment(segmentJson));
                }
            }

            if (!current.TryGetProperty("nextUri", out var nextUriProp) || nextUriProp.ValueKind != JsonValueKind.String)
            {
                break;
            }

            using var follow = await client.GetAsync(nextUriProp.GetString()).ConfigureAwait(false);
            follow.EnsureSuccessStatusCode();
            current = await follow.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        }

        return segments;
    }

    /// <summary>Extracts each spooled segment's object key (the path component after the bucket name) from its presigned <c>uri</c>.</summary>
    public static string ObjectKeyOf(Uri segmentUri)
    {
        // Path is "/<bucket>/<key...>" under path-style access (s3.path-style-access=true).
        var path = segmentUri.AbsolutePath.TrimStart('/');
        var slash = path.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? path : Uri.UnescapeDataString(path[(slash + 1)..]);
    }

    private static Segment ToSegment(JsonElement segmentJson)
    {
        var type = segmentJson.GetProperty("type").GetString()!;
        Uri? segmentUri = segmentJson.TryGetProperty("uri", out var uriProp) ? new Uri(uriProp.GetString()!) : null;
        Uri? ackUri = segmentJson.TryGetProperty("ackUri", out var ackProp) ? new Uri(ackProp.GetString()!) : null;
        string? inlineData = segmentJson.TryGetProperty("data", out var dataProp) ? dataProp.GetString() : null;

        Dictionary<string, string[]>? headers = null;
        if (segmentJson.TryGetProperty("headers", out var headersProp) && headersProp.ValueKind == JsonValueKind.Object)
        {
            headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in headersProp.EnumerateObject())
            {
                headers[property.Name] = [.. property.Value.EnumerateArray().Select(v => v.GetString()!)];
            }
        }

        return new Segment(type, segmentUri, ackUri, headers, inlineData);
    }
}
