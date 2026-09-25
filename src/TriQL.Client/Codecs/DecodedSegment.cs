using System.Buffers;

namespace TriQL.Client.Codecs;

/// <summary>
/// A decoded segment payload: either the original bytes passed through verbatim (the <c>json</c>
/// codec never copies), or a buffer rented from <see cref="ArrayPool{T}.Shared"/> that MUST be
/// returned via <see cref="Dispose"/> — on every path, including exceptional ones (FR-5.3.5).
/// </summary>
internal readonly struct DecodedSegment : IDisposable
{
    private readonly byte[]? _rented;

    private DecodedSegment(ReadOnlyMemory<byte> bytes, byte[]? rented)
    {
        Bytes = bytes;
        _rented = rented;
    }

    /// <summary>The decoded bytes. Valid only until <see cref="Dispose"/> is called when backed by a rented buffer.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Wraps bytes that need no pooled buffer (e.g. an uncompressed segment passed through unchanged).</summary>
    public static DecodedSegment FromExisting(ReadOnlyMemory<byte> bytes) => new(bytes, null);

    /// <summary>Wraps the first <paramref name="length"/> bytes of a buffer rented from <see cref="ArrayPool{T}.Shared"/>.</summary>
    public static DecodedSegment FromRented(byte[] buffer, int length) => new(buffer.AsMemory(0, length), buffer);

    /// <summary>Returns the rented buffer, if any, to <see cref="ArrayPool{T}.Shared"/>.</summary>
    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
        }
    }
}
