using System.Globalization;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Codecs;

/// <summary>
/// The single gate every codec decode passes through, enforcing the decompression-bomb bound
/// (SEC-6, FR-5.3.4): a decoded segment exceeding <c>max(uncompressedSize, ceiling)</c> raises
/// <see cref="TrinoProtocolException"/> rather than allocating unbounded memory. Defense in depth
/// alongside each codec's own bound check.
/// </summary>
internal static class BoundedDecoder
{
    /// <summary>
    /// Decodes <paramref name="payload"/> via <paramref name="codec"/>, bounded by
    /// <c>max(uncompressedSizeHint, ceilingBytes)</c>. The caller owns the returned
    /// <see cref="DecodedSegment"/> and MUST dispose it.
    /// </summary>
    public static DecodedSegment Decode(ISegmentCodec codec, ReadOnlyMemory<byte> payload, long? uncompressedSizeHint, long ceilingBytes)
    {
        // FR-5.3.4's bound is max(uncompressedSize, ceiling): a segment whose server-declared size
        // legitimately exceeds the configured ceiling is still decoded up to that declared size —
        // the ceiling is the floor of trust for an *undeclared* size, not a hard cap that a large
        // but honest declaration cannot exceed. What the bound protects against is the decoded
        // output disagreeing with whichever of the two was larger (a corrupt or malicious payload
        // producing more than either promised or the ceiling permits).
        var maxOutputBytes = Math.Max(uncompressedSizeHint ?? 0, ceilingBytes);

        var decoded = codec.Decode(payload, uncompressedSizeHint, maxOutputBytes);
        if (decoded.Bytes.Length > maxOutputBytes)
        {
            decoded.Dispose();
            throw new TrinoProtocolException(string.Create(
                CultureInfo.InvariantCulture,
                $"A decoded spooled segment was {decoded.Bytes.Length} bytes, exceeding the configured decompression ceiling of {maxOutputBytes} bytes."));
        }

        return decoded;
    }
}
