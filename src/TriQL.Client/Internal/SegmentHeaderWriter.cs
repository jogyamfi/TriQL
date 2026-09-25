namespace TriQL.Client.Internal;

/// <summary>
/// Attaches the per-segment <c>headers</c> the server supplies alongside a spooled segment (e.g.
/// SSE-C key material, or headers a pre-signed object-storage URL requires) to the segment fetch
/// and its acknowledgement.
/// </summary>
internal static class SegmentHeaderWriter
{
    public static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, IReadOnlyList<string>>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }
}
