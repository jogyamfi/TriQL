namespace TriQL.Client.Internal;

/// <summary>The two segment kinds of the spooled protocol (FR-5.2.1).</summary>
internal enum SegmentKind
{
    /// <summary>A base64 payload embedded directly in the page response.</summary>
    Inline,

    /// <summary>A payload fetched from an object-storage or coordinator-proxied <c>uri</c>.</summary>
    Spooled,
}

/// <summary>
/// One entry of a spooled page's <c>segments</c> array, parsed from the wire shape but not yet
/// fetched or decoded. See FR-5.2.1.
/// </summary>
/// <param name="Kind">Whether the payload is embedded (<see cref="SegmentKind.Inline"/>) or fetched (<see cref="SegmentKind.Spooled"/>).</param>
/// <param name="RowOffset">The zero-based row offset of this segment's first row within the query's overall result (FR-5.2.4).</param>
/// <param name="RowsCount">
/// The row count the server declares for this segment, when present. Optional: Trino did not
/// enforce this as a mandatory response field before server release 475 (see the divergence note
/// in the Phase 5 implementation report); below that, it may be absent even on a floor-466 server.
/// </param>
/// <param name="SegmentSize">The exact byte length the fetched/decoded-from-base64 payload MUST have before decompression (FR-5.2.6).</param>
/// <param name="UncompressedSize">
/// The exact byte length the payload MUST have after decompression, when present. Its absence
/// means the server did not compress this particular segment (it was below the server's
/// compression threshold) regardless of the page's negotiated <c>encoding</c> — see the divergence
/// note on <see cref="SegmentClient"/>.
/// </param>
/// <param name="InlineDataBase64">The base64 payload, present only when <paramref name="Kind"/> is <see cref="SegmentKind.Inline"/>.</param>
/// <param name="SegmentUri">The URI to <c>GET</c> the payload from, present only when <paramref name="Kind"/> is <see cref="SegmentKind.Spooled"/>.</param>
/// <param name="AckUri">The URI to acknowledge consumption to, present only when <paramref name="Kind"/> is <see cref="SegmentKind.Spooled"/>.</param>
/// <param name="Headers">
/// Extra per-segment headers the server supplied (e.g. SSE-C key material or a pre-signed URL's
/// required headers) to attach to both the segment fetch and its acknowledgement.
/// </param>
internal sealed record SegmentDescriptor(
    SegmentKind Kind,
    long RowOffset,
    long? RowsCount,
    long SegmentSize,
    long? UncompressedSize,
    string? InlineDataBase64,
    Uri? SegmentUri,
    Uri? AckUri,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Headers);

/// <summary>
/// The parsed <c>encoding</c>+<c>segments</c> shape of a spooled-protocol page's <c>data</c> member
/// (FR-5.1.2), awaiting resolution by <see cref="SegmentClient"/>.
/// </summary>
internal sealed record SpooledPageData(string Encoding, IReadOnlyList<SegmentDescriptor> Segments);
