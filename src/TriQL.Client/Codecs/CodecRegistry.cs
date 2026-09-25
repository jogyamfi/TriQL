namespace TriQL.Client.Codecs;

/// <summary>
/// The registry of segment codecs, keyed by wire encoding name. Built-in <c>json</c> is always
/// present; <c>json+lz4</c>/<c>json+zstd</c> are registered by <c>TriQL.Client.Compression</c>
/// calling <see cref="Register"/> when an application references that package, keeping
/// <c>TriQL.Client</c> dependency-free (REQ-ARCH-4). Extensible so an Arrow codec can be added in
/// 1.1 without a breaking change (FR-5.3.6). Never populated reflectively (NFR-COMPAT-3).
/// </summary>
internal static class CodecRegistry
{
    private static readonly object Gate = new();

    private static readonly Dictionary<string, ISegmentCodec> Codecs =
        new(StringComparer.Ordinal) { [JsonCodec.Instance.Name] = JsonCodec.Instance };

    /// <summary>The built-in uncompressed codec, used both for <c>json</c> and as the fallback for uncompressed segments.</summary>
    public static ISegmentCodec Json => JsonCodec.Instance;

    /// <summary>Registers (or replaces) the codec for a wire encoding name. Called by <c>TriQL.Client.Compression</c>.</summary>
    public static void Register(string name, ISegmentCodec codec)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(codec);

        lock (Gate)
        {
            Codecs[name] = codec;
        }
    }

    /// <summary>Attempts to resolve the codec for <paramref name="name"/>.</summary>
    public static bool TryGet(string name, out ISegmentCodec codec)
    {
        lock (Gate)
        {
            return Codecs.TryGetValue(name, out codec!);
        }
    }
}
