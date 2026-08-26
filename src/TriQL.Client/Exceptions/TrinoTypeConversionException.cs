namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when a wire value could not be converted to the requested CLR type. See FR-12.1.
/// </summary>
public sealed class TrinoTypeConversionException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoTypeConversionException"/> class.</summary>
    public TrinoTypeConversionException(string message)
        : base(message, innerException: null, queryId: null, isRetryable: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TrinoTypeConversionException"/> class with an inner exception.</summary>
    public TrinoTypeConversionException(string message, Exception innerException)
        : base(message, innerException, queryId: null, isRetryable: false)
    {
    }
}
