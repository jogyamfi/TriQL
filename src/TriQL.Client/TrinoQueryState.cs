namespace TriQL.Client;

/// <summary>
/// The client-observed lifecycle state of a query. See FR-4.5.1.
/// </summary>
public enum TrinoQueryState
{
    /// <summary>The initial <c>POST /v1/statement</c> has not yet completed.</summary>
    Submitting,

    /// <summary>The query was accepted and is being polled via <c>nextUri</c>.</summary>
    Running,

    /// <summary>The query completed successfully; <c>nextUri</c> is absent.</summary>
    Finished,

    /// <summary>The query failed, either at submission or via a page-level <c>error</c>.</summary>
    Failed,

    /// <summary>The caller cancelled the query.</summary>
    Cancelled,

    /// <summary>The configured <see cref="TrinoSessionOptions.QueryTimeout"/> elapsed.</summary>
    TimedOut,
}
