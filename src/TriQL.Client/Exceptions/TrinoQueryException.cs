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

    /// <summary>
    /// Whether the failure is transient: resubmitting the same statement may succeed (for example
    /// the cluster was starting up or out of memory, a worker was lost, or a concurrent Iceberg
    /// commit conflicted). Classified from <see cref="ErrorName"/>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TrinoException.IsRetryable"/>, which stays <see langword="false"/>
    /// for query failures: TriQL never resubmits a statement itself, because a resubmitted DML
    /// statement could apply twice. This property is for callers' own retry policies, such as
    /// EF Core's execution strategy.
    /// </remarks>
    public bool IsTransient => TransientErrorNames.Contains(ErrorName);

    /// <summary>The <see cref="ErrorName"/> values <see cref="IsTransient"/> treats as transient.</summary>
    private static readonly HashSet<string> TransientErrorNames = new(StringComparer.Ordinal)
    {
        // Coordinator/cluster availability.
        "SERVER_STARTING_UP",
        "SERVER_SHUTTING_DOWN",
        "NO_NODES_AVAILABLE",
        "QUERY_QUEUE_FULL",
        "CLUSTER_OUT_OF_MEMORY",

        // Lost or unreachable workers and inter-node transport.
        "TOO_MANY_REQUESTS_FAILED",
        "REMOTE_TASK_ERROR",
        "REMOTE_TASK_MISMATCH",
        "REMOTE_HOST_GONE",
        "PAGE_TRANSPORT_ERROR",
        "PAGE_TRANSPORT_TIMEOUT",

        // Optimistic-concurrency conflict between concurrent Iceberg writers.
        "ICEBERG_COMMIT_ERROR",
    };
}
