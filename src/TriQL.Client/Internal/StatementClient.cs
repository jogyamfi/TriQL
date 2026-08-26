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
    private Uri? _lastNextUri;
    private Uri? _lastPartialCancelUri;
    private int _cancelRequested;

    public StatementClient(HttpMessageInvoker invoker, TrinoSessionOptions options, TrinoSession session, ILogger? logger)
    {
        _invoker = invoker;
        _options = options;
        _session = session;
        _logger = logger;
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
            () => new HttpRequestMessage(HttpMethod.Get, pollUri),
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
        if (target is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource(CancelTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, target);
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

        StatementResponseDto dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync(TriqlInternalJsonContext.Default.StatementResponseDto, cancellationToken).ConfigureAwait(false)
                ?? throw new TrinoProtocolException($"The response body from {requestUri} was empty.");
        }
        catch (JsonException ex)
        {
            throw new TrinoProtocolException($"The response from {requestUri} could not be parsed.", ex);
        }

        var envelope = StatementResponseMapper.ToEnvelope(dto, previousColumns);
        _lastNextUri = envelope.NextUri;
        if (envelope.PartialCancelUri is not null)
        {
            _lastPartialCancelUri = envelope.PartialCancelUri;
        }

        return envelope;
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxDiagnosticBodyBytes ? body[..MaxDiagnosticBodyBytes] : body;
    }
}
