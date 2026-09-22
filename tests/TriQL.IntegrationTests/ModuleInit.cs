using System.Runtime.CompilerServices;
using TriQL.Client.Compression;

namespace TriQL.IntegrationTests;

/// <summary>
/// Registers the <c>json+lz4</c>/<c>json+zstd</c> spooled-protocol codecs once for the whole test
/// assembly, exactly as a real application using <c>TriQL.Client.Compression</c> is documented to do
/// at startup (see <see cref="CompressionCodecs.RegisterAll"/>'s own remarks). Without this, any
/// Phase 7 test that lets a spooling-configured coordinator pick a compressed encoding fails with
/// <c>TrinoProtocolException</c> naming the encoding, not because the server or the client's wire
/// handling is broken, but simply because nothing asked the compression package to register itself.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize() => CompressionCodecs.RegisterAll();
}
