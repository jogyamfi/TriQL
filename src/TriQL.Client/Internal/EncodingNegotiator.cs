using TriQL.Client.Codecs;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Internal;

/// <summary>
/// Negotiation-time checks for the spooled protocol (FR-5.1). Shape detection (array vs.
/// <c>encoding</c>+<c>segments</c> object, FR-5.1.2) lives in <see cref="StatementResponseMapper"/>
/// because it must run synchronously against the raw response bytes; this type owns the one
/// negotiation decision that can only be made once a page's segments are about to be resolved:
/// whether the server's chosen encoding is one this client can decode.
/// </summary>
internal static class EncodingNegotiator
{
    /// <summary>
    /// Raises <see cref="TrinoProtocolException"/> naming <paramref name="encoding"/> when no codec
    /// is registered for it (FR-5.1.4).
    /// </summary>
    public static void ValidateEncodingSupported(string encoding)
    {
        if (!CodecRegistry.TryGet(encoding, out _))
        {
            throw new TrinoProtocolException(
                $"The server selected spooled data encoding '{encoding}', which this client cannot decode. " +
                "Narrow TrinoSessionOptions.QueryDataEncodings to encodings this client supports " +
                "(built-in: 'json'; reference TriQL.Client.Compression and call CompressionCodecs.RegisterAll() " +
                "once at startup for 'json+lz4' and 'json+zstd').");
        }
    }
}
