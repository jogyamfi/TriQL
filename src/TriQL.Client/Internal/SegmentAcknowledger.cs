using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;

namespace TriQL.Client.Internal;

/// <summary>
/// Fires spooled-segment acknowledgements (<c>GET ackUri</c>) without blocking the consumer
/// (FR-5.2.3): retried via <see cref="RetryPolicy"/>, and a failure — even after retries — is
/// logged at <see cref="LogLevel.Warning"/> rather than failing the query, since a failed
/// acknowledgement only leaks server-side storage. Every in-flight acknowledgement is tracked so
/// <see cref="DrainAsync"/> can make a best-effort wait for them during cancellation (FR-5.2.7).
/// </summary>
internal sealed class SegmentAcknowledger
{
    private readonly ConcurrentDictionary<Task, byte> _pending = new();
    private readonly ILogger? _logger;

    public SegmentAcknowledger(ILogger? logger)
    {
        _logger = logger;
    }

    /// <summary>Schedules the acknowledgement; returns immediately.</summary>
    public void Acknowledge(
        Uri ackUri,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? segmentHeaders,
        HttpMessageInvoker invoker,
        TrinoSessionOptions options,
        ITrinoAuthenticator authenticator,
        Uri coordinatorOrigin)
    {
        var task = SendAsync(ackUri, segmentHeaders, invoker, options, authenticator, coordinatorOrigin);
        _pending[task] = 0;
        _ = task.ContinueWith(static (t, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(t, out _), _pending, TaskScheduler.Default);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for outstanding acknowledgements to finish. Never
    /// throws: acknowledgements that do not complete in time are abandoned, exactly as any other
    /// acknowledgement failure is (FR-5.2.7).
    /// </summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        var snapshot = _pending.Keys.ToArray();
        if (snapshot.Length == 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await Task.WhenAll(snapshot).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort sweep: acknowledgements still outstanding after the bound are abandoned.
        }
    }

    private async Task SendAsync(
        Uri ackUri,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? segmentHeaders,
        HttpMessageInvoker invoker,
        TrinoSessionOptions options,
        ITrinoAuthenticator authenticator,
        Uri coordinatorOrigin)
    {
        try
        {
            UriGuard.ValidateAckUri(ackUri, coordinatorOrigin);

            using var response = await RequestExecutor.SendAsync(
                invoker,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, ackUri);
                    SegmentHeaderWriter.Apply(request, segmentHeaders);
                    return request;
                },
                authenticator,
                options,
                _logger,
                CancellationToken.None).ConfigureAwait(false);

            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.NoContent) && _logger is not null)
            {
                Log.SegmentAcknowledgementFailedWithStatus(_logger, ackUri, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // FR-5.2.3: acknowledgement failure is never allowed to fail the query.
            if (_logger is not null)
            {
                Log.SegmentAcknowledgementFailed(_logger, ackUri, ex);
            }
        }
    }
}
