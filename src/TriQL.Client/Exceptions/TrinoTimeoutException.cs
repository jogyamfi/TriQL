namespace TriQL.Client.Exceptions;

/// <summary>
/// Raised when a client-side query deadline (<see cref="TrinoSessionOptions.QueryTimeout"/>) is exceeded.
/// Distinct from <see cref="OperationCanceledException"/>, which signals caller-initiated cancellation. See FR-4.6.5, FR-12.1.
/// </summary>
public sealed class TrinoTimeoutException : TrinoException
{
    /// <summary>Initializes a new instance of the <see cref="TrinoTimeoutException"/> class.</summary>
    public TrinoTimeoutException(string message, TimeSpan configuredTimeout, TimeSpan elapsed, string? queryId = null)
        : base(message, innerException: null, queryId, isRetryable: false)
    {
        ConfiguredTimeout = configuredTimeout;
        Elapsed = elapsed;
    }

    /// <summary>The configured <see cref="TrinoSessionOptions.QueryTimeout"/> that elapsed.</summary>
    public TimeSpan ConfiguredTimeout { get; }

    /// <summary>The actual elapsed time when the deadline was enforced.</summary>
    public TimeSpan Elapsed { get; }
}
