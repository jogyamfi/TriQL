using System.Threading;
using TriQL.Client.Codecs;

namespace TriQL.Client.Compression;

/// <summary>
/// Registers the <c>json+lz4</c> and <c>json+zstd</c> spooled-protocol codecs (FR-5.3.2, FR-5.3.3)
/// with <c>TriQL.Client</c>'s internal codec registry. Call <see cref="RegisterAll"/> once during
/// application startup — before executing a query whose <see cref="TrinoSessionOptions.QueryDataEncodings"/>
/// includes <c>json+lz4</c> or <c>json+zstd"</c> — the same explicit, non-reflective registration
/// pattern <c>TriQL.Client.Auth</c>'s authenticators use (NFR-COMPAT-3, FR-5.3.6).
/// </summary>
public static class CompressionCodecs
{
    private static int _registered;

    /// <summary>
    /// Registers the <c>json+lz4</c> and <c>json+zstd</c> codecs. Idempotent and safe to call more
    /// than once (e.g. from multiple modules of the same application).
    /// </summary>
    public static void RegisterAll()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        CodecRegistry.Register("json+lz4", new Lz4Codec());
        CodecRegistry.Register("json+zstd", new ZstdCodec());
    }
}
