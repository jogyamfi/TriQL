namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised on authentication failure: a 401/403 response, or credential acquisition failure. See FR-12.1.
/// </summary>
public sealed class TrinoAuthenticationException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoAuthenticationException"/> class.</summary>
    public TrinoAuthenticationException(string message)
        : base(message, innerException: null, queryId: null, isRetryable: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TrinoAuthenticationException"/> class with an inner exception.</summary>
    public TrinoAuthenticationException(string message, Exception innerException)
        : base(message, innerException, queryId: null, isRetryable: false)
    {
    }
}
