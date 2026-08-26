using Microsoft.Extensions.Logging;

namespace TriQL.Client.Internal;

/// <summary>
/// Backoff parameters for <see cref="RetryPolicy"/>. Defaults match FR-3.3.1: base 50 ms, factor 2.0, cap 10 s, 5 attempts.
/// </summary>
internal readonly record struct RetryPolicyOptions(TimeSpan BaseDelay, double Multiplier, TimeSpan MaxDelay, int MaxAttempts, bool RetryOnResponseStatus = true)
{
    public static RetryPolicyOptions Default { get; } = new(TimeSpan.FromMilliseconds(50), 2.0, TimeSpan.FromSeconds(10), 5);
}

/// <summary>
/// Exponential backoff with full jitter for idempotent HTTP requests. See FR-3.3.1, FR-3.3.2, FR-3.3.2a.
/// </summary>
internal static class RetryPolicy
{
    private static readonly HashSet<int> RetryableStatusCodes = [429, 502, 503, 504];

    /// <summary>
    /// Executes <paramref name="sendAttempt"/>, retrying on retryable status codes and transient
    /// transport failures per <paramref name="options"/>. The caller must build a fresh
    /// <see cref="HttpRequestMessage"/> on every attempt.
    /// </summary>
    public static async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAttempt,
        RetryPolicyOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                response = await sendAttempt(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex))
            {
                var delay = ComputeBackoffDelay(attempt, options);
                if (logger is not null)
                {
                    Log.RetryingRequest(logger, null, attempt, delay, ex.GetType().Name);
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (attempt >= options.MaxAttempts || !options.RetryOnResponseStatus || !RetryableStatusCodes.Contains((int)response.StatusCode))
            {
                return response;
            }

            var retryAfter = ParseRetryAfter(response);
            var requestUri = response.RequestMessage?.RequestUri;
            var statusCode = response.StatusCode;
            response.Dispose();

            var backoffDelay = retryAfter ?? ComputeBackoffDelay(attempt, options);
            if (logger is not null)
            {
                Log.RetryingRequest(logger, requestUri, attempt, backoffDelay, statusCode.ToString());
            }

            await Task.Delay(backoffDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or IOException || (ex is TriQL.Client.Exceptions.TrinoException { IsRetryable: true });

    private static TimeSpan ComputeBackoffDelay(int attempt, RetryPolicyOptions options)
    {
        var exponential = options.BaseDelay.TotalMilliseconds * Math.Pow(options.Multiplier, attempt - 1);
        var capped = Math.Min(exponential, options.MaxDelay.TotalMilliseconds);
        var jittered = Random.Shared.NextDouble() * capped;
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }
}
