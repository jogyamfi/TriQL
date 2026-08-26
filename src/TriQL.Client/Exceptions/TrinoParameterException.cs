namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when parameter binding, count, or type validation fails before a request is sent. See FR-12.1.
/// </summary>
public sealed class TrinoParameterException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoParameterException"/> class.</summary>
    public TrinoParameterException(string message)
        : base(message, innerException: null, queryId: null, isRetryable: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TrinoParameterException"/> class with an inner exception.</summary>
    public TrinoParameterException(string message, Exception innerException)
        : base(message, innerException, queryId: null, isRetryable: false)
    {
    }
}
