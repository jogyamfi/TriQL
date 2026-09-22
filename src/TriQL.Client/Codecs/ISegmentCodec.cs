namespace TriQL.Client.Codecs;

/// <summary>
/// Decodes a spooled-protocol segment payload into decompressed JSON bytes (an array-of-arrays
/// <c>data</c> shape, ready for <see cref="Internal.Utf8RowDecoder"/>). See FR-5.3, FR-5.3.6.
/// </summary>
/// <remarks>
/// Kept internal per FR-5.3.6: the codec set is extensible so an Arrow codec can be added in 1.1
/// without a breaking change, but that extension point is exercised by TriQL itself (via
/// <c>InternalsVisibleTo</c>, the same non-reflective pattern <c>AuthenticatorRegistry</c> uses),
/// not by arbitrary third-party assemblies.
/// </remarks>
internal interface ISegmentCodec
{
    /// <summary>The wire encoding name this codec handles, e.g. <c>json</c>, <c>json+lz4</c>, <c>json+zstd</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Decodes <paramref name="payload"/>. <paramref name="uncompressedSizeHint"/> is
    /// <c>metadata.uncompressedSize</c> when the server supplied it; implementations that require a
    /// known output size (e.g. LZ4 block decoding) MUST throw <see cref="Exceptions.TrinoProtocolException"/>
    /// when it is <see langword="null"/>. <paramref name="maxOutputBytes"/> is the decompression
    /// ceiling (SEC-6); implementations MUST NOT allocate or write beyond it.
    /// </summary>
    DecodedSegment Decode(ReadOnlyMemory<byte> payload, long? uncompressedSizeHint, long maxOutputBytes);
}
