using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using TriQL.Client;
using TriQL.Client.Exceptions;
using TriQL.Data.ADO.Internal;

namespace TriQL.Data.ADO;

/// <summary>
/// A <see cref="DbConnection"/> to a Trino coordinator. See FR-9.1.
/// </summary>
/// <remarks>
/// A single command may execute at a time per connection (FR-9.1.15); attempting to start a
/// second concurrent command throws <see cref="InvalidOperationException"/> rather than
/// corrupting session state.
/// </remarks>
public sealed class TrinoConnection : DbConnection
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private TrinoSessionOptions? _options;
    private TrinoClient? _client;
    private ConnectionState _state = ConnectionState.Closed;
    private string? _connectionString;
    private string? _serverVersion;
    private int _commandActive;

    /// <summary>Initializes a new, unconfigured instance. <see cref="ConnectionString"/> or <see cref="TrinoSessionOptions"/> must be set before <see cref="Open"/>.</summary>
    public TrinoConnection()
    {
    }

    /// <summary>Initializes a new instance with the given connection string.</summary>
    public TrinoConnection(string connectionString)
        : this() => ConnectionString = connectionString;

    /// <summary>Initializes a new instance for dependency-injection scenarios (FR-9.1.16).</summary>
    public TrinoConnection(TrinoSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Initializes a new instance that creates its <see cref="HttpClient"/> via <paramref name="httpClientFactory"/> (FR-9.1.16).</summary>
    public TrinoConnection(TrinoSessionOptions options, IHttpClientFactory httpClientFactory)
        : this(options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Raised with query progress statistics and query failures (FR-9.1.14).</summary>
    public event EventHandler<TrinoInfoMessageEventArgs>? InfoMessage;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The connection is not <see cref="ConnectionState.Closed"/> (FR-1.3.6).</exception>
    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString ?? string.Empty;
        set
        {
            if (_state != ConnectionState.Closed)
            {
                throw new InvalidOperationException("ConnectionString can only be set while the connection is Closed (FR-1.3.6).");
            }

            _connectionString = value;
            _options = string.IsNullOrEmpty(value) ? null : new TrinoConnectionStringBuilder(value).ToSessionOptions();
        }
    }

    /// <inheritdoc/>
    public override string Database => _client?.Session.Schema ?? _options?.Schema ?? string.Empty;

    /// <inheritdoc/>
    public override string DataSource => _options?.Server?.ToString() ?? string.Empty;

    /// <inheritdoc/>
    public override string ServerVersion => _serverVersion ??
        throw new InvalidOperationException("ServerVersion is unavailable: the connection has not confirmed a server version yet.");

    /// <inheritdoc/>
    public override ConnectionState State => _state;

    /// <inheritdoc/>
    public override int ConnectionTimeout => (int)(_options?.RequestTimeout ?? TimeSpan.FromSeconds(100)).TotalSeconds;

    /// <inheritdoc/>
    protected override DbProviderFactory DbProviderFactory => TrinoProviderFactory.Instance;

    /// <summary>The live session, once open.</summary>
    internal TrinoSession? Session => _client?.Session;

    /// <inheritdoc/>
    public override void Open() => SyncBridge.Run(() => OpenAsync(CancellationToken.None));

    /// <inheritdoc/>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_state == ConnectionState.Open)
        {
            return;
        }

        if (_options is null)
        {
            throw new InvalidOperationException("ConnectionString or TrinoSessionOptions must be set before Open().");
        }

        SetState(ConnectionState.Connecting);
        try
        {
            var invoker = _httpClientFactory?.CreateClient(nameof(TrinoConnection));
            var client = invoker is null ? new TrinoClient(_options) : new TrinoClient(_options, invoker);

            if (_options.TestConnectionOnOpen)
            {
                var info = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
                if (info.Starting)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw new TrinoConnectionException("The Trino server reported starting = true.");
                }

                _serverVersion = info.Version;
            }

            _client = client;
            SetState(ConnectionState.Open);
        }
        catch
        {
            SetState(ConnectionState.Closed);
            throw;
        }
    }

    /// <inheritdoc/>
    public override void Close() => SyncBridge.Run(CloseAsync);

    /// <inheritdoc/>
    public override async Task CloseAsync()
    {
        if (_state == ConnectionState.Closed)
        {
            return;
        }

        var client = _client;
        _client = null;
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        SetState(ConnectionState.Closed);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException"><paramref name="databaseName"/> is null or empty.</exception>
    public override void ChangeDatabase(string databaseName) => SyncBridge.Run(() => ChangeDatabaseAsync(databaseName, CancellationToken.None));

    /// <summary>Sets the session schema (FR-9.1.9).</summary>
    public override async Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        var client = RequireOpenClient();
        var resultSet = await client.ExecuteAsync($"USE {IdentifierQuoting.Quote(databaseName)}", cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (resultSet.ConfigureAwait(false))
        {
            await resultSet.DrainAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand() => new TrinoCommand { Connection = this };

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always. TriQL does not support transactions (FR-9.1.11).</exception>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException("TriQL does not support transactions (FR-9.1.11).");

    /// <inheritdoc/>
    public override DataTable GetSchema() => SyncBridge.Run(() => GetSchemaAsync());

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName) => SyncBridge.Run(() => GetSchemaAsync(collectionName));

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) =>
        SyncBridge.Run(() => GetSchemaAsync(collectionName, restrictionValues));

    /// <inheritdoc/>
    /// <remarks>The async form of the parameterless <see cref="GetSchema()"/> (FR-9.5.6).</remarks>
    // RS0026: these three overloads each carry an optional cancellationToken because they override
    // DbConnection's own fixed, pre-existing virtual signatures — not a new API design choice.
#pragma warning disable RS0026
    public override Task<DataTable> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        GetSchemaAsync(SchemaCollections.MetaDataCollectionsName, [], cancellationToken);

    /// <inheritdoc/>
    /// <remarks>The async form of <see cref="GetSchema(string)"/> (FR-9.5.6).</remarks>
    public override Task<DataTable> GetSchemaAsync(string collectionName, CancellationToken cancellationToken = default) =>
        GetSchemaAsync(collectionName, [], cancellationToken);

    /// <inheritdoc/>
    /// <remarks>The async form of <see cref="GetSchema(string, string?[])"/> (FR-9.5.6).</remarks>
    public override async Task<DataTable> GetSchemaAsync(string collectionName, string?[] restrictionValues, CancellationToken cancellationToken = default)
#pragma warning restore RS0026
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        ArgumentNullException.ThrowIfNull(restrictionValues);

        var client = string.Equals(collectionName, SchemaCollections.MetaDataCollectionsName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(collectionName, "DataTypes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collectionName, "ReservedWords", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collectionName, "Restrictions", StringComparison.OrdinalIgnoreCase)
            ? _client
            : RequireOpenClient();

        var restrictions = restrictionValues.Length == 0 ? null : restrictionValues;
        return await SchemaCollections.GetSchemaAsync(client, collectionName, restrictions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requires the connection to be open and returns the underlying <see cref="TrinoClient"/>.</summary>
    internal TrinoClient RequireOpenClient() =>
        _client is not null && _state == ConnectionState.Open
            ? _client
            : throw new InvalidOperationException("The connection is not open.");

    /// <summary>Enforces single-command-at-a-time execution (FR-9.1.15).</summary>
    internal void BeginCommand()
    {
        if (Interlocked.CompareExchange(ref _commandActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one command may execute at a time per connection (FR-9.1.15).");
        }
    }

    /// <summary>Releases the single-command-at-a-time lock taken by <see cref="BeginCommand"/>.</summary>
    internal void EndCommand() => Volatile.Write(ref _commandActive, 0);

    /// <summary>Raises <see cref="InfoMessage"/> (FR-9.1.14).</summary>
    internal void RaiseInfoMessage(TrinoQueryStats? stats, Exception? error) =>
        InfoMessage?.Invoke(this, new TrinoInfoMessageEventArgs(stats, error));

    private void SetState(ConnectionState newState)
    {
        if (_state == newState)
        {
            return;
        }

        var old = _state;
        _state = newState;
        OnStateChange(new StateChangeEventArgs(old, newState));
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _state != ConnectionState.Closed)
        {
            Close();
        }

        base.Dispose(disposing);
    }
}
