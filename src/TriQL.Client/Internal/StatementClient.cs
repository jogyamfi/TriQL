using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// Drives a single query's <c>POST /v1/statement</c> submission, <c>nextUri</c> advance loop, and
/// <c>DELETE</c> cancellation. See FR-4.1, FR-4.3, FR-4.6.
/// </summary>
internal sealed class StatementClient
{
    private const int MaxDiagnosticBodyBytes = 8192;
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpMessageInvoker _invoker;
    private readonly TrinoSessionOptions _options;
    private readonly TrinoSession _session;
    private readonly ILogger? _logger;
    private readonly SegmentAcknowledger _segmentAcknowledger;
    private readonly SegmentClient _segmentClient;
    private Uri? _lastNextUri;
    private Uri? _lastPartialCancelUri;
    private int _cancelRequested;

    public StatementClient(HttpMessageInvoker invoker, TrinoSessionOptions options, TrinoSession session, ILogger? logger)
    {
        _invoker = invoker;
        _options = options;
        _session = session;
        _logger = logger;
        _segmentAcknowledger = new SegmentAcknowledger(logger);
        _segmentClient = new SegmentClient(invoker, options, logger, _segmentAcknowledger);
    }

    /// <summary>
    /// <c>POST {server}/v1/statement</c> (FR-4.1.1). Retried only on pre-dispatch (connection)
    /// failures, never on a response received after dispatch, to avoid duplicate query execution (FR-3.3.3).
    /// </summary>
    public async Task<TrinoPageEnvelope> SubmitAsync(string sql, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(_options.Server!, "v1/statement");

        using var response = await RequestExecutor.SendAsync(
            _invoker,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(sql, Encoding.UTF8, "text/plain"),
                };
                ProtocolHeaders.WriteSessionHeaders(request, _options, _session);
                return request;
            },
            _options.Authenticator ?? AnonymousAuthenticator.Instance,
            _options,
            _logger,
            cancellationToken,
            retryOnResponseStatus: false).ConfigureAwait(false);

        return await ReadEnvelopeAsync(response, requestUri, previousColumns: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Follows <paramref name="current"/>'s <c>nextUri</c> (FR-4.3.1).</summary>
    public async Task<TrinoPageEnvelope> AdvanceAsync(TrinoPageEnvelope current, CancellationToken cancellationToken)
    {
        var nextUri = current.NextUri ?? throw new InvalidOperationException("AdvanceAsync requires a page with a nextUri.");
        var pollUri = BuildPollUri(nextUri);

        using var response = await RequestExecutor.SendAsync(
            _invoker,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, pollUri);
                ProtocolHeaders.WriteFollowUpHeaders(request, _options);
                return request;
            },
            _options.Authenticator ?? AnonymousAuthenticator.Instance,
            _options,
            _logger,
            cancellationToken).ConfigureAwait(false);

        return await ReadEnvelopeAsync(response, pollUri, current.Columns, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>DELETE</c>s the last known <c>nextUri</c> (falling back to <c>partialCancelUri</c>) to
    /// terminate the query server-side. Idempotent; bounded by its own timeout, never the caller's
    /// (possibly already-cancelled) token; failures are logged, never thrown. See FR-4.6.1—FR-4.6.4.
    /// </summary>
    public async Task CancelAsync()
    {
        if (Interlocked.Exchange(ref _cancelRequested, 1) == 1)
        {
            return;
        }

        var target = _lastNextUri ?? _lastPartialCancelUri;
        if (target is not null)
        {
            using var cts = new CancellationTokenSource(CancelTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, target);
                ProtocolHeaders.WriteFollowUpHeaders(request, _options);
                using var response = await _invoker.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent) && _logger is not null)
                {
                    Log.CancellationRequestFailedWithStatus(_logger, target, (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                {
                    Log.CancellationRequestFailed(_logger, target, ex);
                }
            }
        }

        // FR-5.2.7: best-effort ack sweep, bounded by the same 10 s cancellation timeout as the
        // DELETE above, so abandoning a query does not leave cheaply-acknowledgeable segments
        // unacknowledged on the object store.
        await _segmentAcknowledger.DrainAsync(CancelTimeout).ConfigureAwait(false);
    }

    private Uri BuildPollUri(Uri nextUri)
    {
        // FR-4.3.2/FR-4.3.3: built fresh from a copy on every call; the stored nextUri is never mutated in place.
        if (!nextUri.AbsolutePath.Contains("/executing", StringComparison.Ordinal))
        {
            return nextUri;
        }

        var builder = new UriBuilder(nextUri);
        var targetResultSize = FormatTargetResultSize(_options.TargetResultSizeBytes);
        var existingQuery = builder.Query.Length > 1 ? builder.Query[1..] + "&" : string.Empty;
        builder.Query = $"{existingQuery}targetResultSize={targetResultSize}";
        return builder.Uri;
    }

    private static string FormatTargetResultSize(long bytes)
    {
        var megabytes = Math.Max(1, bytes / (1024 * 1024));
        return string.Create(CultureInfo.InvariantCulture, $"{megabytes}MB");
    }

    private async Task<TrinoPageEnvelope> ReadEnvelopeAsync(
        HttpResponseMessage response, Uri requestUri, IReadOnlyList<TrinoColumn>? previousColumns, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await ReadTruncatedBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw new TrinoQueryException(
                $"{requestUri} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
                queryId: null,
                errorCode: 0,
                errorName: "HTTP_ERROR",
                errorType: TrinoErrorType.InternalError);
        }

        _session.Apply(SessionMutationApplier.Parse(response));

        // Buffered as one contiguous array because RawJsonSlice offsets index into exactly this buffer,
        // which Utf8RowDecoder then slices to decode rows without re-materializing them (NFR-PERF-3).
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        StatementResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(responseBytes, TriqlInternalJsonContext.Default.StatementResponseDto)
                ?? throw new TrinoProtocolException($"The response body from {requestUri} was empty.");
        }
        catch (JsonException ex)
        {
            throw new TrinoProtocolException($"The response from {requestUri} could not be parsed.", ex);
        }

        var envelope = StatementResponseMapper.ToEnvelope(dto, responseBytes, previousColumns, requestUri);
        _lastNextUri = envelope.NextUri;
        if (envelope.PartialCancelUri is not null)
        {
            _lastPartialCancelUri = envelope.PartialCancelUri;
        }

        // FR-5.1.2/FR-5.2: the mapper only detects the spooled shape and parses its segment
        // descriptors synchronously; resolving them is async I/O, done here since this method
        // already runs in an async context (see SegmentClient's remarks for why).
        if (envelope.PendingSpooling is { } pending)
        {
            var rows = await _segmentClient.ResolveAsync(pending, envelope.Columns, cancellationToken).ConfigureAwait(false);
            envelope = envelope with { Rows = rows, ValuesAreDecoded = true, PendingSpooling = null };
        }

        return envelope;
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxDiagnosticBodyBytes ? body[..MaxDiagnosticBodyBytes] : body;
    }
}
