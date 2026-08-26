namespace TriQL.Client.Exceptions;

/// <summary>
/// The severity/category Trino assigns to a query failure. See FR-12.1.2.
/// </summary>
public enum TrinoErrorType
{
    /// <summary>An error caused by the user's query or input.</summary>
    UserError,

    /// <summary>An internal server error.</summary>
    InternalError,

    /// <summary>The server lacked sufficient resources to run the query.</summary>
    InsufficientResources,

    /// <summary>An error originating outside the coordinator, e.g. a connector or remote system.</summary>
    External,
}

/// <summary>
/// The source location within the query text associated with a <see cref="TrinoQueryException"/>. See FR-12.1.2.
/// </summary>
/// <param name="LineNumber">The 1-based line number.</param>
/// <param name="ColumnNumber">The 1-based column number.</param>
public sealed record TrinoErrorLocation(int LineNumber, int ColumnNumber);

/// <summary>
/// The server-reported failure detail attached to a <see cref="TrinoQueryException"/>. See FR-12.1.2.
/// </summary>
/// <param name="Type">The fully qualified server-side exception type name.</param>
/// <param name="Message">The failure message.</param>
/// <param name="Suppressed">Suppressed nested failures, if any.</param>
/// <param name="Stack">The server-side stack trace frames, if provided.</param>
public sealed record TrinoFailureInfo(
    string Type,
    string? Message,
    IReadOnlyList<TrinoFailureInfo> Suppressed,
    IReadOnlyList<string> Stack);

/// <summary>
/// Raised when the coordinator reports a query failure. See FR-12.1.2.
/// </summary>
public sealed class TrinoQueryException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoQueryException"/> class.</summary>
    public TrinoQueryException(
        string message,
        string? queryId,
        int errorCode,
        string errorName,
        TrinoErrorType errorType,
        TrinoFailureInfo? failureInfo = null,
        TrinoErrorLocation? errorLocation = null)
        : base(message, innerException: null, queryId, isRetryable: false)
    {
        ErrorCode = errorCode;
        ErrorName = errorName;
        ErrorType = errorType;
        FailureInfo = failureInfo;
        ErrorLocation = errorLocation;
    }

    /// <summary>The numeric Trino error code.</summary>
    public int ErrorCode { get; }

    /// <summary>The Trino error name, e.g. <c>SYNTAX_ERROR</c>.</summary>
    public string ErrorName { get; }

    /// <summary>The error category.</summary>
    public TrinoErrorType ErrorType { get; }

    /// <summary>The server-reported failure detail, when available.</summary>
    public TrinoFailureInfo? FailureInfo { get; }

    /// <summary>The location in the query text the error refers to, when available.</summary>
    public TrinoErrorLocation? ErrorLocation { get; }
}
