namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when the coordinator cannot be reached or a connection cannot be opened. See FR-12.1.
/// </summary>
public sealed class TrinoConnectionException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoConnectionException"/> class.</summary>
    public TrinoConnectionException(string message, Exception? innerException = null, bool isRetryable = true)
        : base(message, innerException, queryId: null, isRetryable)
    {
    }
}
