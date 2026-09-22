using System.Net;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Codecs;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Internal;

/// <summary>
/// Resolves a <see cref="SpooledPageData"/> descriptor into decoded rows, in <c>rowOffset</c> order
/// (FR-5.2.4), fetching <see cref="SegmentKind.Spooled"/> segments concurrently up to
/// <see cref="TrinoSessionOptions.SegmentFetchParallelism"/> (FR-5.2.5) and scheduling a
/// fire-and-forget acknowledgement for each one once its rows have decoded successfully (FR-5.2.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Divergence from requirements.md FR-5.2.1/FR-5.2.2</b> (see the Phase 5 implementation
/// report): the real protocol's segment <c>metadata</c> object carries <c>rowOffset</c> and
/// <c>segmentSize</c> always, but <c>uncompressedSize</c> only when the server actually compressed
/// that segment, and <c>rowsCount</c> only reliably from server release 475 onward (Trino issue
/// history documents it as not enforced as mandatory before then, and this client's floor is 466).
/// A segment whose metadata omits <c>uncompressedSize</c> is decoded as plain uncompressed JSON —
/// via the built-in <c>json</c> codec — regardless of the page's negotiated top-level
/// <c>encoding</c>; this is not a TriQL choice, it mirrors both the official Python and Go client
/// implementations. Separately, FR-5.2.2 says segment fetches "MUST carry the session's
/// authentication credential"; the reference clients instead attach the session credential only
/// when the segment/ack host matches the coordinator's origin (the "coordinator-proxied" case
/// FR-5.2.2 itself describes), and otherwise rely solely on the server-supplied per-segment
/// <c>headers</c> — sending a Trino bearer token to an off-origin object store would both do
/// nothing useful and leak the credential. This client follows the reference clients' behaviour.
/// </para>
/// </remarks>
internal sealed class SegmentClient
{
    private readonly HttpMessageInvoker _invoker;
    private readonly TrinoSessionOptions _options;
    private readonly ILogger? _logger;
    private readonly SegmentAcknowledger _acknowledger;
    private readonly Uri _coordinatorOrigin;
    private readonly HashSet<string> _loggedSegmentHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hostLogGate = new();

    public SegmentClient(HttpMessageInvoker invoker, TrinoSessionOptions options, ILogger? logger, SegmentAcknowledger acknowledger)
    {
        _invoker = invoker;
        _options = options;
        _logger = logger;
        _acknowledger = acknowledger;
        _coordinatorOrigin = options.Server!;
    }

    public async Task<object?[][]> ResolveAsync(
        SpooledPageData pending, IReadOnlyList<TrinoColumn>? columns, CancellationToken cancellationToken)
    {
        if (pending.Segments.Count == 0)
        {
            return [];
        }

        if (columns is not { Count: > 0 })
        {
            throw new TrinoProtocolException("A spooled data page was received before column metadata was available.");
        }

        // FR-5.1.4: validated once per page, even for a segment that turns out not to need
        // decompression, since the encoding was negotiated at the query level.
        EncodingNegotiator.ValidateEncodingSupported(pending.Encoding);

        using var semaphore = new SemaphoreSlim(Math.Max(1, _options.SegmentFetchParallelism));
        var perSegmentTasks = new Task<object?[][]>[pending.Segments.Count];
        for (var i = 0; i < pending.Segments.Count; i++)
        {
            perSegmentTasks[i] = ResolveOneAsync(pending.Segments[i], pending.Encoding, columns, semaphore, cancellationToken);
        }

        var perSegmentRows = await Task.WhenAll(perSegmentTasks).ConfigureAwait(false);

        // FR-5.2.4: delivery order follows rowOffset, independent of fetch/decode completion order.
        var order = new int[pending.Segments.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => pending.Segments[a].RowOffset.CompareTo(pending.Segments[b].RowOffset));

        var totalRows = 0;
        foreach (var rows in perSegmentRows)
        {
            totalRows += rows.Length;
        }

        var combined = new object?[totalRows][];
        var cursor = 0;
        foreach (var i in order)
        {
            var rows = perSegmentRows[i];
            Array.Copy(rows, 0, combined, cursor, rows.Length);
            cursor += rows.Length;
        }

        return combined;
    }

    private async Task<object?[][]> ResolveOneAsync(
        SegmentDescriptor segment, string encoding, IReadOnlyList<TrinoColumn> columns, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return segment.Kind == SegmentKind.Inline
                ? DecodeInline(segment, encoding, columns)
                : await FetchAndDecodeSpooledAsync(segment, encoding, columns, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private object?[][] DecodeInline(SegmentDescriptor segment, string encoding, IReadOnlyList<TrinoColumn> columns)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(segment.InlineDataBase64!);
        }
        catch (FormatException ex)
        {
            throw new TrinoProtocolException("An inline spooled segment's base64 payload could not be decoded.", ex);
        }

        return DecodeSegmentBytes(payload, segment, encoding, columns);
    }

    private async Task<object?[][]> FetchAndDecodeSpooledAsync(
        SegmentDescriptor segment, string encoding, IReadOnlyList<TrinoColumn> columns, CancellationToken cancellationToken)
    {
        var uri = segment.SegmentUri!;
        UriGuard.ValidateSegmentUri(uri, _options.SegmentHostAllowlist);
        LogSegmentHostOnFirstUse(uri);

        var authenticator = _options.Authenticator ?? AnonymousAuthenticator.Instance;
        // See the type-level remarks: the session credential travels only to a coordinator-proxied
        // (same-origin) segment endpoint, never to off-origin object storage.
        var effectiveAuthenticator = UriGuard.IsSameOrigin(uri, _coordinatorOrigin) ? authenticator : AnonymousAuthenticator.Instance;

        var response = await RequestExecutor.SendAsync(
            _invoker,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                SegmentHeaderWriter.Apply(request, segment.Headers);
                return request;
            },
            effectiveAuthenticator,
            _options,
            _logger,
            cancellationToken).ConfigureAwait(false);

        byte[] payload;
        try
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new TrinoProtocolException($"The spooled segment fetch from '{uri}' returned status {(int)response.StatusCode}.");
            }

            payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }

        var rows = DecodeSegmentBytes(payload, segment, encoding, columns);

        // FR-5.2.3: fire-and-forget, scheduled only after the segment's rows decoded successfully.
        _acknowledger.Acknowledge(segment.AckUri!, segment.Headers, _invoker, _options, authenticator, _coordinatorOrigin);
        if (_logger is not null && _logger.IsEnabled(LogLevel.Debug))
        {
            Log.SegmentFetched(_logger, uri, payload.LongLength);
        }

        return rows;
    }

    private object?[][] DecodeSegmentBytes(byte[] payload, SegmentDescriptor segment, string encoding, IReadOnlyList<TrinoColumn> columns)
    {
        if (payload.LongLength != segment.SegmentSize)
        {
            throw new TrinoProtocolException(
                $"A spooled segment payload was {payload.LongLength} bytes, but metadata declared segmentSize {segment.SegmentSize}.");
        }

        // See the type-level remarks: a segment lacking uncompressedSize was left uncompressed by
        // the server regardless of the page's negotiated encoding.
        ISegmentCodec codec;
        if (segment.UncompressedSize is null)
        {
            codec = CodecRegistry.Json;
        }
        else if (!CodecRegistry.TryGet(encoding, out codec))
        {
            // Already validated in ResolveAsync; defensive only.
            throw new TrinoProtocolException($"The server selected spooled data encoding '{encoding}', which this client cannot decode.");
        }

        using var decoded = BoundedDecoder.Decode(codec, payload, segment.UncompressedSize, _options.MaxDecompressedSegmentBytes);

        if (segment.UncompressedSize is { } expectedUncompressed && decoded.Bytes.Length != expectedUncompressed)
        {
            throw new TrinoProtocolException(
                $"A decoded spooled segment was {decoded.Bytes.Length} bytes, but metadata declared uncompressedSize {expectedUncompressed}.");
        }

        var rows = Utf8RowDecoder.DecodeRows(decoded.Bytes.Span, columns);

        if (segment.RowsCount is { } expectedRows && rows.Length != expectedRows)
        {
            throw new TrinoProtocolException(
                $"A decoded spooled segment produced {rows.Length} rows, but metadata declared rowsCount {expectedRows}.");
        }

        return rows;
    }

    private void LogSegmentHostOnFirstUse(Uri segmentUri)
    {
        if (_logger is null || !_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        bool firstUse;
        lock (_hostLogGate)
        {
            firstUse = _loggedSegmentHosts.Add(segmentUri.Host);
        }

        if (firstUse)
        {
            Log.SegmentHostFirstUse(_logger, segmentUri.Host);
        }
    }
}
