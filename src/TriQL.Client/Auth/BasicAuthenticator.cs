namespace TriQL.Client.Auth;

/// <summary>
/// Sends <c>Authorization: Basic base64(user:password)</c>. See FR-2.2.2.
/// </summary>
public class BasicAuthenticator : ITrinoAuthenticator, ITransmitsBearerCredential
{
    private readonly string _username;
    private readonly string _password;

    /// <summary>Initializes a new instance of the <see cref="BasicAuthenticator"/> class.</summary>
    public BasicAuthenticator(string username, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentNullException.ThrowIfNull(password);

        _username = username;
        _password = password;
    }

    /// <summary>
    /// The Basic username. Used as the default <c>X-Trino-User</c> when
    /// <see cref="TrinoSessionOptions.User"/> is not explicitly set (FR-2.2.2).
    /// </summary>
    public string Username => _username;

    /// <inheritdoc/>
    public virtual ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public virtual ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AuthenticatorHelpers.SetBasicCredential(request, _username, _password);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    /// <inheritdoc/>
    public virtual void ConfigureHandler(SocketsHttpHandler handler)
    {
    }
}
