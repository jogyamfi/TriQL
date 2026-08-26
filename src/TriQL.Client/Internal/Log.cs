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
}
