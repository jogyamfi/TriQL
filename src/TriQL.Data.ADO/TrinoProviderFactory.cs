using System.Data.Common;

namespace TriQL.Data.ADO;

/// <summary>
/// The <see cref="DbProviderFactory"/> for Trino. See FR-9.4.1, FR-9.4.2.
/// </summary>
/// <remarks>
/// Register with <c>DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance)</c>.
/// </remarks>
public sealed class TrinoProviderFactory : DbProviderFactory
{
    /// <summary>The ADO.NET provider invariant name.</summary>
    public const string InvariantName = "TriQL.Data.Trino";

    /// <summary>The shared factory singleton.</summary>
    public static readonly TrinoProviderFactory Instance = new();

    private TrinoProviderFactory()
    {
    }

    /// <inheritdoc/>
    public override bool CanCreateCommandBuilder => false;

    /// <inheritdoc/>
    public override bool CanCreateDataAdapter => false;

    /// <inheritdoc/>
    public override bool CanCreateBatch => false;

    /// <inheritdoc/>
    public override DbConnection CreateConnection() => new TrinoConnection();

    /// <inheritdoc/>
    public override DbCommand CreateCommand() => new TrinoCommand();

    /// <inheritdoc/>
    public override DbParameter CreateParameter() => new TrinoDbParameter();

    /// <inheritdoc/>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new TrinoConnectionStringBuilder();

    /// <inheritdoc/>
    public override DbDataSource CreateDataSource(string connectionString) => new TrinoDataSource(connectionString);
}
