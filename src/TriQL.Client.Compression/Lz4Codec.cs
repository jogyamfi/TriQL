using System.Buffers;
using K4os.Compression.LZ4;
using TriQL.Client.Codecs;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Compression;

/// <summary>
/// The <c>json+lz4</c> spooled-segment codec (FR-5.3.2). The payload is raw LZ4 block-compressed
/// data with no frame header, so the exact uncompressed length — <c>metadata.uncompressedSize</c> —
/// MUST be known before decoding.
/// </summary>
internal sealed class Lz4Codec : ISegmentCodec
{
    public string Name => "json+lz4";

    public DecodedSegment Decode(ReadOnlyMemory<byte> payload, long? uncompressedSizeHint, long maxOutputBytes)
    {
        if (uncompressedSizeHint is not { } hint)
        {
            throw new TrinoProtocolException(
                "An LZ4-encoded spooled segment is missing metadata.uncompressedSize, which LZ4 block decoding requires to size its output buffer.");
        }

        // maxOutputBytes is already max(hint, ceiling) (see BoundedDecoder), so hint can never
        // exceed it — the only bound left to enforce here is what a managed array can hold. A
        // declared size beyond that cannot be a legitimate segment regardless of what the ceiling
        // would otherwise permit; reject cleanly rather than letting the checked cast below throw
        // an unhandled OverflowException.
        if (hint > Array.MaxLength)
        {
            throw new TrinoProtocolException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"An LZ4 segment's declared uncompressed size ({hint} bytes) cannot be allocated (exceeds {Array.MaxLength} bytes)."));
        }

        var length = checked((int)hint);
        var target = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = LZ4Codec.Decode(payload.Span, target.AsSpan(0, length));
            if (written != length)
            {
                throw new TrinoProtocolException(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"LZ4 decoding of a spooled segment produced {written} bytes, but metadata declared uncompressedSize {length}."));
            }

            return DecodedSegment.FromRented(target, written);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(target);
            throw;
        }
    }
}
