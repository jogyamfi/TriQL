using System.Buffers;
using TriQL.Client.Codecs;
using TriQL.Client.Exceptions;
using ZstdSharp;

namespace TriQL.Client.Compression;

/// <summary>
/// The <c>json+zstd</c> spooled-segment codec (FR-5.3.3), using the vetted, actively maintained
/// <c>ZstdSharp.Port</c> package recorded in requirements.md §23 (the closed G2 decision) since no
/// BCL Zstandard facility exists on <c>net10.0</c>.
/// </summary>
internal sealed class ZstdCodec : ISegmentCodec
{
    public string Name => "json+zstd";

    public DecodedSegment Decode(ReadOnlyMemory<byte> payload, long? uncompressedSizeHint, long maxOutputBytes)
    {
        if (uncompressedSizeHint is not { } hint)
        {
            throw new TrinoProtocolException(
                "A Zstandard-encoded spooled segment is missing metadata.uncompressedSize, which this codec requires to size its output buffer.");
        }

        // See Lz4Codec's equivalent guard and remarks: maxOutputBytes is already max(hint,
        // ceiling), so only a managed array's own size limit remains to enforce here.
        if (hint > Array.MaxLength)
        {
            throw new TrinoProtocolException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"A Zstandard segment's declared uncompressed size ({hint} bytes) cannot be allocated (exceeds {Array.MaxLength} bytes)."));
        }

        var length = checked((int)hint);
        var target = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int written;
            using (var decompressor = new Decompressor())
            {
                try
                {
                    written = decompressor.Unwrap(payload.Span, target.AsSpan(0, length));
                }
                catch (ZstdException ex)
                {
                    throw new TrinoProtocolException("A Zstandard-encoded spooled segment could not be decompressed.", ex);
                }
            }

            if (written != length)
            {
                throw new TrinoProtocolException(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Zstandard decoding of a spooled segment produced {written} bytes, but metadata declared uncompressedSize {length}."));
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
