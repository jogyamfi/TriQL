using TriQL.Client.Exceptions;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Decides whether a failure is worth retrying. A statement Trino reports as failed for a transient
/// reason (<see cref="TrinoQueryException.IsTransient"/>: cluster starting or out of memory, a lost
/// worker, an <c>ICEBERG_COMMIT_ERROR</c> between concurrent writers, …) did not commit, so it is safe to
/// run again. A transport failure (<see cref="TrinoException.IsRetryable"/>) is retried for whole
/// operations, but not for a single <c>SaveChanges</c> statement: the statement may have committed before
/// the connection was lost.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public static class TrinoTransientExceptionDetector
{
    /// <summary>
    /// Marks failures that must not be retried as a whole. EF's execution strategy unwraps a
    /// <c>DbUpdateException</c> before deciding whether to retry, so a <c>SaveChanges</c> failure is
    /// recognised by this marker on every exception in its chain.
    /// </summary>
    private const string NoRetryKey = "TriQL.NoRetry";

    /// <summary>
    /// Marks <paramref name="exception"/> and its inner exceptions so the execution strategy does not
    /// retry them: a failed <c>SaveChanges</c> may already have committed earlier statements.
    /// </summary>
    public static void MarkNotRetryable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            current.Data[NoRetryKey] = true;
        }
    }

    /// <summary>
    /// Whether an operation that failed with <paramref name="exception"/> can be run again as a whole
    /// (a query, <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, or anything run through the execution strategy).
    /// </summary>
    public static bool ShouldRetryOn(Exception? exception) =>
        exception?.Data.Contains(NoRetryKey) != true
        && Chain(exception).Any(e => e is TrinoQueryException { IsTransient: true } or TrinoException { IsRetryable: true });

    /// <summary>
    /// Whether a single <c>SaveChanges</c> statement that failed with <paramref name="exception"/> can be
    /// run again: only when Trino reported the statement itself as failed for a transient reason.
    /// </summary>
    public static bool ShouldRetryStatementOn(Exception? exception) =>
        Chain(exception).Any(e => e is TrinoQueryException { IsTransient: true });

    /// <summary>
    /// The delay before retry number <paramref name="attempt"/> (starting at 1): exponential backoff from
    /// 200 ms with ±20% jitter, capped at <paramref name="maxDelay"/>. Jitter spreads concurrent writers
    /// that conflicted, so their retries do not collide again.
    /// </summary>
    public static TimeSpan GetDelay(int attempt, TimeSpan maxDelay)
    {
#pragma warning disable CA5394 // Jitter for retry spreading, not security.
        var exponential = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) * (0.8 + (Random.Shared.NextDouble() * 0.4)));
#pragma warning restore CA5394
        return exponential < maxDelay ? exponential : maxDelay;
    }

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
