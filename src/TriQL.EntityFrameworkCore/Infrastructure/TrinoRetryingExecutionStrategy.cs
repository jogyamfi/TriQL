using Microsoft.EntityFrameworkCore.Storage;
using TriQL.EntityFrameworkCore.Storage.Internal;

namespace TriQL.EntityFrameworkCore.Infrastructure;

/// <summary>
/// An execution strategy that retries operations failing for a transient reason: the cluster starting
/// up, out of memory or losing a worker, a conflicting concurrent Iceberg commit
/// (<c>ICEBERG_COMMIT_ERROR</c>), or a lost connection. Enable it with
/// <c>UseTrino(..., o =&gt; o.EnableRetryOnFailure())</c>.
/// </summary>
/// <remarks>
/// <para>
/// Queries, <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> are retried as a whole. <c>SaveChanges</c> is
/// not: Trino has no multi-statement transactions, so re-running a whole save could repeat statements that
/// already committed. The provider instead retries each failed statement on its own, and only when Trino
/// reported that statement as failed (a connection lost mid-statement may have committed it).
/// </para>
/// <para>
/// EF does not run <c>ExecuteSql</c> through the execution strategy. To retry raw SQL, wrap it:
/// <c>await context.Database.CreateExecutionStrategy().ExecuteAsync(() =&gt; context.Database.ExecuteSqlAsync(...))</c>.
/// </para>
/// </remarks>
public class TrinoRetryingExecutionStrategy : ExecutionStrategy
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="dependencies">Parameter object containing service dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    public TrinoRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount, TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
    }

    /// <inheritdoc />
    protected override bool ShouldRetryOn(Exception exception) => TrinoTransientExceptionDetector.ShouldRetryOn(exception);
}
