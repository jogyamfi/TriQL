using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Diagnostics;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Internal;

/// <summary>
/// Executes a single logical HTTP exchange: applies authentication (with refresh-on-401/403 per
/// FR-2.1.3), enforces the per-request timeout via a linked <see cref="CancellationTokenSource"/>
/// distinct from the query deadline (FR-3.1.5), and retries transient failures via
/// <see cref="RetryPolicy"/>.
/// </summary>
internal static class RequestExecutor
{
    // Reverse proxies and WAFs in front of a coordinator commonly reject requests with no
    // User-Agent; HttpClient sends none by default, so every request gets one here.
    private static readonly ProductInfoHeaderValue DefaultUserAgent = new(
        "TriQL",
        typeof(RequestExecutor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(RequestExecutor).Assembly.GetName().Version?.ToString()
            ?? "1.0.0");

    public static Task<HttpResponseMessage> SendAsync(
        HttpMessageInvoker invoker,
        Func<HttpRequestMessage> requestFactory,
        ITrinoAuthenticator authenticator,
        TrinoSessionOptions options,
        ILogger? logger,
        CancellationToken cancellationToken,
        bool retryOnResponseStatus = true) =>
        RetryPolicy.ExecuteAsync(
            ct => SendOnceAsync(invoker, requestFactory, authenticator, options, logger, ct),
            RetryPolicyOptions.Default with { RetryOnResponseStatus = retryOnResponseStatus },
            logger,
            cancellationToken);

    private static async Task<HttpResponseMessage> SendOnceAsync(
        HttpMessageInvoker invoker,
        Func<HttpRequestMessage> requestFactory,
        ITrinoAuthenticator authenticator,
        TrinoSessionOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.RequestTimeout);

        var attemptedRefresh = false;

        while (true)
        {
            using var request = requestFactory();
            if (request.Headers.UserAgent.Count == 0)
            {
                request.Headers.UserAgent.Add(DefaultUserAgent);
            }

            await authenticator.ApplyAsync(request, cancellationToken).ConfigureAwait(false);

            using var requestActivity = Tracing.StartRequestActivity(request.Method, request.RequestUri);
            Tracing.PropagateTraceContext(request);

            if (logger is not null && logger.IsEnabled(LogLevel.Trace))
            {
#pragma warning disable CA1873 // Guarded by the IsEnabled check above; DumpHeaders is only evaluated when Trace logging is active.
                Log.RequestHeaders(logger, request.Method.Method, request.RequestUri, Redactor.DumpHeaders(request.Headers));
#pragma warning restore CA1873
            }

            var response = await SendWithTimeoutAsync(invoker, request, options.RequestTimeout, timeoutCts.Token, cancellationToken).ConfigureAwait(false);
            Tracing.SetResponseStatus(requestActivity, (int)response.StatusCode);

            if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                return response;
            }

            if (attemptedRefresh)
            {
                var status = (int)response.StatusCode;
                var detail = await ReadFailureDetailAsync(response, cancellationToken).ConfigureAwait(false);
                response.Dispose();
                throw new TrinoAuthenticationException($"Authentication failed with status {status} for '{request.RequestUri}'.{detail}");
            }

            attemptedRefresh = true;
            var refreshed = await authenticator.TryRefreshAsync(response, cancellationToken).ConfigureAwait(false);
            var failureStatus = (int)response.StatusCode;
            var failureDetail = await ReadFailureDetailAsync(response, cancellationToken).ConfigureAwait(false);
            response.Dispose();

            if (!refreshed)
            {
                throw new TrinoAuthenticationException(
                    $"Authentication failed with status {failureStatus} for '{request.RequestUri}' and the configured authenticator could not refresh its credential.{failureDetail}");
            }
        }
    }

    /// <summary>Reads a short excerpt of a failed response body so the server's own explanation is not lost.</summary>
    private static async Task<string> ReadFailureDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            var collapsed = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (collapsed.Length > 500)
            {
                collapsed = collapsed[..500] + "...";
            }

            return $" Server response: {collapsed}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private static async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpMessageInvoker invoker,
        HttpRequestMessage request,
        TimeSpan requestTimeout,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            return await invoker.SendAsync(request, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new TrinoConnectionException(
                $"The request to '{request.RequestUri}' did not complete within the configured RequestTimeout of {requestTimeout}.");
        }
    }
}
