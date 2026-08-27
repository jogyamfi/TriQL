using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using TriQL.Client;
using TriQL.Client.Exceptions;
using TriQL.Data.ADO.Internal;
using ClientParameter = TriQL.Client.TrinoParameter;
using ClientParameterCollection = TriQL.Client.TrinoParameterCollection;

namespace TriQL.Data.ADO;

/// <summary>
/// A <see cref="DbCommand"/> that executes SQL against a <see cref="TrinoConnection"/>. See FR-9.2.
/// </summary>
public sealed class TrinoCommand : DbCommand
{
    private readonly TrinoParameterCollection _parameters = new();
    private CommandType _commandType = CommandType.Text;
    private TrinoConnection? _connection;
    private TrinoResultSet? _activeResultSet;
    private int _commandTimeout;

    /// <summary>Initializes a new instance with no command text or connection.</summary>
    public TrinoCommand()
    {
    }

    /// <summary>Initializes a new instance with the given command text.</summary>
    public TrinoCommand(string commandText) => CommandText = commandText;

    /// <inheritdoc/>
    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException"><see cref="CommandType.StoredProcedure"/> or <see cref="CommandType.TableDirect"/> was set.</exception>
    public override CommandType CommandType
    {
        get => _commandType;
        set
        {
            if (value != CommandType.Text)
            {
                throw new NotSupportedException($"TriQL only supports {nameof(CommandType.Text)} commands.");
            }

            _commandType = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>0</c> means unbounded. Per-command only; never mutates the connection's shared options (FR-9.2.3).</remarks>
    public override int CommandTimeout { get => _commandTimeout; set => _commandTimeout = value; }

    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource
    {
        get => UpdateRowSource.None;
        set
        {
            if (value != UpdateRowSource.None)
            {
                throw new NotSupportedException("TriQL only supports UpdateRowSource.None.");
            }
        }
    }

    /// <inheritdoc/>
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc/>
    [AllowNull]
    protected override DbConnection DbConnection
    {
        get => _connection!;
        set => _connection = (TrinoConnection?)value;
    }

    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <summary>The strongly typed parameter collection (FR-9.4.4).</summary>
    public new TrinoParameterCollection Parameters => _parameters;

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => null;
        set
        {
            if (value is not null)
            {
                throw new NotSupportedException("TriQL does not support transactions (FR-9.1.11).");
            }
        }
    }

    /// <inheritdoc/>
    public override void Cancel() => _activeResultSet?.Dispose();

    /// <inheritdoc/>
    public override void Prepare()
    {
        // FR-9.2.8: a documented no-op, since some tooling calls this unconditionally.
    }

    /// <inheritdoc/>
    protected override DbParameter CreateDbParameter() => new TrinoDbParameter();

    /// <inheritdoc/>
    public override int ExecuteNonQuery() => SyncBridge.Run(() => ExecuteNonQueryAsync(CancellationToken.None));

    /// <inheritdoc/>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        connection.BeginCommand();
        try
        {
            var resultSet = await ExecuteCoreAsync(connection, cancellationToken).ConfigureAwait(false);
            _activeResultSet = resultSet;
            await using (resultSet.ConfigureAwait(false))
            {
                long rowCount = 0;
                await foreach (var _ in resultSet.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
                {
                    rowCount++;
                }

                var affected = resultSet.UpdateCount ?? rowCount;
                return affected > int.MaxValue ? int.MaxValue : (int)affected;
            }
        }
        finally
        {
            _activeResultSet = null;
            connection.EndCommand();
        }
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar() => SyncBridge.Run(() => ExecuteScalarAsync(CancellationToken.None));

    /// <inheritdoc/>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        connection.BeginCommand();
        try
        {
            var resultSet = await ExecuteCoreAsync(connection, cancellationToken).ConfigureAwait(false);
            _activeResultSet = resultSet;
            await using (resultSet.ConfigureAwait(false))
            {
                await foreach (var row in resultSet.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
                {
                    return row.FieldCount > 0 ? row.GetValue(0) : null;
                }

                return null;
            }
        }
        finally
        {
            _activeResultSet = null;
            connection.EndCommand();
        }
    }

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        SyncBridge.Run(() => ExecuteDbDataReaderAsync(behavior, CancellationToken.None));

    /// <inheritdoc/>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var connection = RequireConnection();
        connection.BeginCommand();
        try
        {
            var resultSet = await ExecuteCoreAsync(connection, cancellationToken).ConfigureAwait(false);
            _activeResultSet = resultSet;

            var closeConnectionOnDispose = (behavior & CommandBehavior.CloseConnection) != 0;
            return await TrinoDataReader.CreateAsync(
                resultSet,
                behavior,
                onClosed: () =>
                {
                    _activeResultSet = null;
                    connection.EndCommand();
                    if (closeConnectionOnDispose)
                    {
                        connection.Close();
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _activeResultSet = null;
            connection.EndCommand();
            throw;
        }
    }

    private async Task<TrinoResultSet> ExecuteCoreAsync(TrinoConnection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(CommandText))
        {
            throw new InvalidOperationException("CommandText must be set before executing a command.");
        }

        var client = connection.RequireOpenClient();
        var parameters = ToClientParameters();

        using var timeoutCts = _commandTimeout > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(_commandTimeout)) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await client.ExecuteAsync(CommandText, parameters, retainPreparedStatement: false, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
        {
            var configuredTimeout = TimeSpan.FromSeconds(_commandTimeout);
            throw new TrinoTimeoutException($"CommandTimeout of {_commandTimeout}s expired.", configuredTimeout, configuredTimeout, queryId: null);
        }
    }

    private ClientParameterCollection? ToClientParameters()
    {
        if (_parameters.Count == 0)
        {
            return null;
        }

        var result = new ClientParameterCollection();
        foreach (TrinoDbParameter parameter in _parameters)
        {
            var value = parameter.Value is DBNull ? null : parameter.Value;
            var clientParameter = new ClientParameter(string.IsNullOrEmpty(parameter.ParameterName) ? null : parameter.ParameterName, value)
            {
                DbType = parameter.HasExplicitDbType ? parameter.DbType : null,
                TrinoType = parameter.TrinoType,
                Precision = parameter.Precision == 0 ? null : parameter.Precision,
                Scale = parameter.Scale == 0 ? null : parameter.Scale,
                Size = parameter.Size == 0 ? null : parameter.Size,
                IsNullable = parameter.IsNullable,
            };
            result.Add(clientParameter);
        }

        return result;
    }

    private TrinoConnection RequireConnection() =>
        _connection ?? throw new InvalidOperationException("Connection must be set before executing a command.");
}
