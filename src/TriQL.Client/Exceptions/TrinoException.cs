namespace TriQL.Client.Exceptions;

/// <summary>
/// Base type for every exception raised by TriQL. See FR-12.1.
/// </summary>
public abstract class TrinoException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TrinoException"/> class.</summary>
    protected TrinoException(string message, Exception? innerException, string? queryId, bool isRetryable)
        : base(message, innerException)
    {
        QueryId = queryId;
        IsRetryable = isRetryable;
    }

    /// <summary>The id of the query in progress when this exception occurred, if known.</summary>
    public string? QueryId { get; }

    /// <summary>Whether the operation that raised this exception may succeed if retried.</summary>
    public bool IsRetryable { get; }
}
