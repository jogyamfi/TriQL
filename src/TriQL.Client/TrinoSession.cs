using TriQL.Client.Internal;

namespace TriQL.Client;

/// <summary>
/// Carries the set of server-driven header names applied by a <see cref="TrinoSession.SessionChanged"/> event. See FR-1.2.6.
/// </summary>
/// <param name="appliedHeaders">The response header names whose mutations were applied.</param>
public sealed class TrinoSessionChangedEventArgs(IReadOnlyList<string> appliedHeaders) : EventArgs
{
    /// <summary>The response header names whose mutations were applied in this batch.</summary>
    public IReadOnlyList<string> AppliedHeaders { get; } = appliedHeaders;
}

/// <summary>
/// The live session state derived from the initial <see cref="TrinoSessionOptions"/> plus every
/// server-driven mutation applied since. Safe for concurrent reads while a statement executes. See FR-1.2.1.
/// </summary>
public sealed class TrinoSession
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _sessionProperties;
    private readonly Dictionary<string, TrinoSelectedRole> _roles;
    private readonly Dictionary<string, string> _preparedStatements;
    private string? _catalog;
    private string? _schema;
    private string? _path;
    private string? _authorizationUser;
    private IReadOnlyList<TrinoSelectedRole> _originalRoles = [];
    private string? _transactionId;

    internal TrinoSession(TrinoSessionOptions options)
    {
        _catalog = options.Catalog;
        _schema = options.Schema;
        _path = options.Path;
        _authorizationUser = options.AuthorizationUser;
        _sessionProperties = new Dictionary<string, string>(options.SessionProperties, StringComparer.Ordinal);
        _roles = new Dictionary<string, TrinoSelectedRole>(options.Roles, StringComparer.Ordinal);
        _preparedStatements = new Dictionary<string, string>(options.PreparedStatements, StringComparer.Ordinal);
    }

    /// <summary>Raised after a batch of server-driven session mutations has been applied.</summary>
    public event EventHandler<TrinoSessionChangedEventArgs>? SessionChanged;

    /// <summary>The current catalog.</summary>
    public string? Catalog { get { lock (_gate) { return _catalog; } } }

    /// <summary>The current schema.</summary>
    public string? Schema { get { lock (_gate) { return _schema; } } }

    /// <summary>The current SQL path.</summary>
    public string? Path { get { lock (_gate) { return _path; } } }

    /// <summary>The current authorization user, or <see langword="null"/> if not set.</summary>
    public string? AuthorizationUser { get { lock (_gate) { return _authorizationUser; } } }

    /// <summary>The current transaction id, or <see langword="null"/> if none is active.</summary>
    public string? TransactionId { get { lock (_gate) { return _transactionId; } } }

    /// <summary>A snapshot of the current session properties.</summary>
    public IReadOnlyDictionary<string, string> SessionProperties
    {
        get { lock (_gate) { return new Dictionary<string, string>(_sessionProperties, StringComparer.Ordinal); } }
    }

    /// <summary>A snapshot of the current role selections, keyed by catalog (empty key for the system role).</summary>
    public IReadOnlyDictionary<string, TrinoSelectedRole> Roles
    {
        get { lock (_gate) { return new Dictionary<string, TrinoSelectedRole>(_roles, StringComparer.Ordinal); } }
    }

    /// <summary>A snapshot of the current prepared statements, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> PreparedStatements
    {
        get { lock (_gate) { return new Dictionary<string, string>(_preparedStatements, StringComparer.Ordinal); } }
    }

    /// <summary>A snapshot of the original user's role selections.</summary>
    public IReadOnlyList<TrinoSelectedRole> OriginalRoles { get { lock (_gate) { return _originalRoles; } } }

    /// <summary>
    /// Applies a batch of server-driven mutations atomically, in the order required by FR-1.2.2,
    /// and raises <see cref="SessionChanged"/> once for the batch.
    /// </summary>
    internal void Apply(SessionMutationBatch batch)
    {
        if (batch.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            if (batch.Catalog is { } catalog)
            {
                _catalog = catalog;
            }

            if (batch.Schema is { } schema)
            {
                _schema = schema;
            }

            if (batch.Path is { } path)
            {
                _path = path;
            }

            foreach (var (key, value) in batch.SetSessionProperties)
            {
                _sessionProperties[key] = value;
            }

            foreach (var key in batch.ClearedSessionProperties)
            {
                _sessionProperties.Remove(key);
            }

            foreach (var (catalogKey, role) in batch.SetRoles)
            {
                _roles[catalogKey] = role;
            }

            if (batch.OriginalRoles is { } originalRoles)
            {
                _originalRoles = originalRoles;
            }

            foreach (var (name, sql) in batch.AddedPreparedStatements)
            {
                _preparedStatements[name] = sql;
            }

            foreach (var name in batch.DeallocatedPreparedStatements)
            {
                _preparedStatements.Remove(name);
            }

            if (batch.SetAuthorizationUser is { } setAuthUser)
            {
                _authorizationUser = setAuthUser;
            }

            if (batch.ResetAuthorizationUser)
            {
                _authorizationUser = null;
            }

            if (batch.StartedTransactionId is { } startedTransactionId)
            {
                _transactionId = startedTransactionId;
            }

            if (batch.ClearedTransactionId)
            {
                _transactionId = null;
            }
        }

        SessionChanged?.Invoke(this, new TrinoSessionChangedEventArgs(batch.AppliedHeaderNames));
    }
}
