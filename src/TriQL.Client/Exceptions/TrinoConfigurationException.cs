namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when <see cref="TrinoSessionOptions"/> or a connection string is invalid. See FR-12.1.
/// </summary>
public sealed class TrinoConfigurationException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoConfigurationException"/> class.</summary>
    public TrinoConfigurationException(string message)
        : base(message, innerException: null, queryId: null, isRetryable: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TrinoConfigurationException"/> class with an inner exception.</summary>
    public TrinoConfigurationException(string message, Exception innerException)
        : base(message, innerException, queryId: null, isRetryable: false)
    {
    }
}
