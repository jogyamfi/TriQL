using System.Text;
using K4os.Compression.LZ4;
using TriQL.Client.Codecs;
using TriQL.Client.Compression;
using TriQL.Client.Exceptions;
using ZstdSharp;

namespace TriQL.Client.Tests.Codecs;

/// <summary>
/// Codec round-trip, bounds, and registry tests (P5-T13): FR-5.3.1—FR-5.3.6, SEC-6.
/// </summary>
public sealed class CodecTests
{
    private static readonly byte[] SamplePayload = Encoding.UTF8.GetBytes("""[[1,"a"],[2,"b"],[3,"c"]]""");

    [Fact]
    public void JsonCodec_PassesPayloadThroughUnchanged()
    {
        var decoded = CodecRegistry.Json.Decode(SamplePayload, uncompressedSizeHint: null, maxOutputBytes: 1_000_000);
        using (decoded)
        {
            Assert.True(decoded.Bytes.Span.SequenceEqual(SamplePayload));
        }
    }

    [Fact]
    public void JsonCodec_PayloadExceedingCeiling_Throws()
    {
        var ex = Assert.Throws<TrinoProtocolException>(
            () => CodecRegistry.Json.Decode(SamplePayload, uncompressedSizeHint: null, maxOutputBytes: SamplePayload.Length - 1));
        Assert.Contains("ceiling", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompressionCodecs_RegisterAll_RegistersLz4AndZstd()
    {
        CompressionCodecs.RegisterAll();

        Assert.True(CodecRegistry.TryGet("json+lz4", out var lz4));
        Assert.Equal("json+lz4", lz4.Name);

        Assert.True(CodecRegistry.TryGet("json+zstd", out var zstd));
        Assert.Equal("json+zstd", zstd.Name);
    }

    [Fact]
    public void Lz4Codec_RoundTripsCompressedPayload()
    {
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet("json+lz4", out var codec));

        var compressed = Lz4Encode(SamplePayload);

        using var decoded = codec.Decode(compressed, SamplePayload.Length, maxOutputBytes: 1_000_000);
        Assert.True(decoded.Bytes.Span.SequenceEqual(SamplePayload));
    }

    [Fact]
    public void Lz4Codec_MissingUncompressedSizeHint_Throws()
    {
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet("json+lz4", out var codec));

        var compressed = Lz4Encode(SamplePayload);

        var ex = Assert.Throws<TrinoProtocolException>(() => codec.Decode(compressed, uncompressedSizeHint: null, maxOutputBytes: 1_000_000));
        Assert.Contains("uncompressedSize", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZstdCodec_RoundTripsCompressedPayload()
    {
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet("json+zstd", out var codec));

        var compressed = ZstdEncode(SamplePayload);

        using var decoded = codec.Decode(compressed, SamplePayload.Length, maxOutputBytes: 1_000_000);
        Assert.True(decoded.Bytes.Span.SequenceEqual(SamplePayload));
    }

    [Fact]
    public void ZstdCodec_MissingUncompressedSizeHint_Throws()
    {
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet("json+zstd", out var codec));

        var compressed = ZstdEncode(SamplePayload);

        var ex = Assert.Throws<TrinoProtocolException>(() => codec.Decode(compressed, uncompressedSizeHint: null, maxOutputBytes: 1_000_000));
        Assert.Contains("uncompressedSize", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("json+lz4")]
    [InlineData("json+zstd")]
    public void BoundedDecoder_DecompressionBomb_RejectedWithoutLeakingRentedBuffer(string encoding)
    {
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet(encoding, out var codec));

        // FR-5.3.4's bound is max(uncompressedSize, ceiling): a large-but-honest declared size is
        // permitted up to that declared size (see BoundedDecoder's remarks), so the actual
        // defense-in-depth here is that no managed array can ever hold a declared size this
        // absurd — SEC-6 must reject cleanly rather than let ArrayPool.Rent or a checked cast crash.
        var ex = Assert.Throws<TrinoProtocolException>(
            () => BoundedDecoder.Decode(codec, SamplePayload, uncompressedSizeHint: 10_000_000_000, ceilingBytes: 1_000_000));
        Assert.Contains("allocated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("json+lz4")]
    [InlineData("json+zstd")]
    public void BoundedDecoder_DeclaredSizeAboveCeiling_StillDecodedUpToDeclaredSize(string encoding)
    {
        // The inverse of the bomb case: a segment honestly declaring more than the configured
        // ceiling is not rejected outright — the ceiling is the trust floor for an undeclared
        // size, not a hard cap on an honest larger one (FR-5.3.4's max(uncompressedSize, ceiling)).
        CompressionCodecs.RegisterAll();
        Assert.True(CodecRegistry.TryGet(encoding, out var codec));

        var compressed = encoding == "json+lz4" ? Lz4Encode(SamplePayload) : ZstdEncode(SamplePayload);

        using var decoded = BoundedDecoder.Decode(codec, compressed, SamplePayload.Length, ceilingBytes: 1);
        Assert.True(decoded.Bytes.Span.SequenceEqual(SamplePayload));
    }

    [Fact]
    public void BoundedDecoder_UsesCeilingWhenNoUncompressedSizeHint()
    {
        // No hint: the ceiling alone bounds `json`'s pass-through, per FR-5.3.4's max(hint, ceiling).
        var ex = Assert.Throws<TrinoProtocolException>(
            () => BoundedDecoder.Decode(CodecRegistry.Json, SamplePayload, uncompressedSizeHint: null, ceilingBytes: SamplePayload.Length - 1));
        Assert.Contains("ceiling", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodedSegment_FromRented_DisposeReturnsBufferWithoutThrowing()
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16);
        var segment = DecodedSegment.FromRented(buffer, 16);
        segment.Dispose();
        // Disposing twice must not throw (defense against a double-dispose bug in a caller's finally block).
        segment.Dispose();
    }

    private static byte[] Lz4Encode(byte[] source)
    {
        var target = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
        var written = LZ4Codec.Encode(source, target);
        return target[..written];
    }

    private static byte[] ZstdEncode(byte[] source)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(source).ToArray();
    }
}
