using System.Data.Common;

namespace TriQL.Data.ADO;

/// <summary>
/// A <see cref="DbDataSource"/> for pooled, DI-friendly <see cref="TrinoConnection"/> creation. See FR-9.4.3.
/// </summary>
public sealed class TrinoDataSource : DbDataSource
{
    private readonly string _connectionString;

    /// <summary>Initializes a new instance for the given connection string.</summary>
    public TrinoDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public override string ConnectionString => _connectionString;

    /// <inheritdoc/>
    protected override DbConnection CreateDbConnection() => new TrinoConnection(_connectionString);
}
