using Microsoft.Extensions.Logging;

namespace TriQL.Client.Internal;

/// <summary>
/// Source-generated log messages. Level assignments follow FR-11.1.3.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "TLS validation weakened: AllowSelfSignedCertificate={AllowSelfSignedCertificate}, AllowHostNameMismatch={AllowHostNameMismatch}.")]
    public static partial void WeakenedTlsValidation(ILogger logger, bool allowSelfSignedCertificate, bool allowHostNameMismatch);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Detected Trino server version {Version}.")]
    public static partial void ServerVersionDetected(ILogger logger, string version);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Retrying request to {RequestUri} (attempt {Attempt}) after {Delay} due to {Reason}.")]
    public static partial void RetryingRequest(ILogger logger, Uri? requestUri, int attempt, TimeSpan delay, string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Trace, Message = "{Method} {RequestUri} headers: {Headers}")]
    public static partial void RequestHeaders(ILogger logger, string method, Uri? requestUri, string headers);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Query {QueryId} submitted.")]
    public static partial void QuerySubmitted(ILogger logger, string queryId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Query {QueryId} completed in {Elapsed} with {RowCount} rows read.")]
    public static partial void QueryCompleted(ILogger logger, string queryId, TimeSpan elapsed, long rowCount);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Cancellation request to {RequestUri} returned status {StatusCode}.")]
    public static partial void CancellationRequestFailedWithStatus(ILogger logger, Uri? requestUri, int statusCode);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Cancellation request to {RequestUri} failed.")]
    public static partial void CancellationRequestFailed(ILogger logger, Uri? requestUri, Exception exception);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "A query progress callback threw an exception.")]
    public static partial void ProgressCallbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Page received for query {QueryId}: {RowCount} rows.")]
    public static partial void PageReceived(ILogger logger, string queryId, int rowCount);
}
