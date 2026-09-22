using TriQL.Client.Exceptions;

namespace TriQL.Client.Codecs;

/// <summary>
/// The built-in, always-registered <c>json</c> (uncompressed) codec. Also used as the effective
/// codec for any segment whose metadata omits <c>uncompressedSize</c>, regardless of the
/// negotiated top-level encoding — see the divergence note on <c>SegmentClient.DecodeSegmentBytes</c>. FR-5.3.1.
/// </summary>
internal sealed class JsonCodec : ISegmentCodec
{
    public static readonly JsonCodec Instance = new();

    private JsonCodec()
    {
    }

    public string Name => "json";

    public DecodedSegment Decode(ReadOnlyMemory<byte> payload, long? uncompressedSizeHint, long maxOutputBytes)
    {
        if (payload.Length > maxOutputBytes)
        {
            throw new TrinoProtocolException(
                $"An uncompressed spooled segment was {payload.Length} bytes, exceeding the configured " +
                $"decompression ceiling of {maxOutputBytes} bytes (TrinoSessionOptions.MaxDecompressedSegmentBytes).");
        }

        return DecodedSegment.FromExisting(payload);
    }
}
