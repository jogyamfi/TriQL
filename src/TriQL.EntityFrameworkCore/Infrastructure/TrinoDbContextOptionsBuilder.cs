using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TriQL.EntityFrameworkCore.Infrastructure.Internal;

namespace TriQL.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Allows Trino-specific configuration to be performed on <see cref="DbContextOptions"/>. Obtained
/// from the optional action passed to <c>UseTrino</c>.
/// </summary>
public class TrinoDbContextOptionsBuilder
    : RelationalDbContextOptionsBuilder<TrinoDbContextOptionsBuilder, TrinoOptionsExtension>
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="optionsBuilder">The core options builder.</param>
    public TrinoDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }

    /// <summary>
    /// Whether connections created from a connection string use a <c>UTC</c> session time zone when
    /// the connection string does not set <c>TimeZone</c>. On by default, so <c>DateTime.Now</c>,
    /// <c>DateTimeOffset</c> parts and timestamp casts give the same results on every machine;
    /// otherwise the session uses the client machine's time zone. Connections supplied as a
    /// <c>DbConnection</c>, <c>TrinoSessionOptions</c> or <c>TrinoDataSource</c> keep their own setting.
    /// </summary>
    /// <param name="useUtcSessionTimeZone"><see langword="false"/> to keep the client machine's time zone.</param>
    /// <returns>The same builder instance so that calls can be chained.</returns>
    public virtual TrinoDbContextOptionsBuilder UseUtcSessionTimeZone(bool useUtcSessionTimeZone = true) =>
        WithOption(extension => extension.WithUseUtcSessionTimeZone(useUtcSessionTimeZone));

    /// <summary>
    /// Retries operations that fail for a transient reason: the cluster starting up, out of memory or losing
    /// a worker, a conflicting concurrent Iceberg commit (<c>ICEBERG_COMMIT_ERROR</c>), or a lost connection.
    /// Queries, <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> are retried as a whole; <c>SaveChanges</c> retries
    /// only the statement that failed, because earlier statements have already committed.
    /// </summary>
    /// <param name="maxRetryCount">The maximum number of retries. Defaults to 6.</param>
    /// <param name="maxRetryDelay">The longest delay between retries. Defaults to 30 seconds.</param>
    /// <returns>The same builder instance so that calls can be chained.</returns>
    public virtual TrinoDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount = 6, TimeSpan? maxRetryDelay = null)
    {
        var delay = maxRetryDelay ?? TimeSpan.FromSeconds(30);
        WithOption(extension => extension.WithRetryOnFailure(maxRetryCount, delay));
        return ExecutionStrategy(dependencies => new TrinoRetryingExecutionStrategy(dependencies, maxRetryCount, delay));
    }
}
