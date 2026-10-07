using System.Data.Common;
using TriQL.Client;

namespace TriQL.Data.ADO;

/// <summary>
/// A <see cref="DbDataSource"/> for pooled, DI-friendly <see cref="TrinoConnection"/> creation. See FR-9.4.3.
/// </summary>
/// <remarks>
/// Every connection created by a data source sends its requests through one shared HTTP handler, so
/// pooled TCP/TLS connections are reused across connections. The handler is built on the first
/// <see cref="TrinoConnection.Open"/> and disposed with the data source; dispose the data source only
/// after the connections created from it.
/// </remarks>
public sealed class TrinoDataSource : DbDataSource
{
    private readonly string _connectionString;
    private readonly Lazy<HttpMessageInvoker> _invoker;

    /// <summary>Initializes a new instance for the given connection string.</summary>
    public TrinoDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _connectionString = connectionString;
        _invoker = new Lazy<HttpMessageInvoker>(
            () => TrinoClient.CreateOwnedInvoker(new TrinoConnectionStringBuilder(connectionString).ToSessionOptions(), loggerFactory: null),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc/>
    public override string ConnectionString => _connectionString;

    /// <inheritdoc/>
    protected override DbConnection CreateDbConnection() => new TrinoConnection(_connectionString, _invoker);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseInvoker();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        ReleaseInvoker();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private void ReleaseInvoker()
    {
        if (_invoker.IsValueCreated)
        {
            // HttpMessageInvoker.Dispose is idempotent, so the sync and async paths may both reach here.
            _invoker.Value.Dispose();
        }
    }
}
