namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when a server response is malformed, unsupported, or internally inconsistent. See FR-12.1.
/// </summary>
public sealed class TrinoProtocolException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoProtocolException"/> class.</summary>
    public TrinoProtocolException(string message, Exception? innerException = null, string? queryId = null)
        : base(message, innerException, queryId, isRetryable: false)
    {
    }
}
