namespace TriQL.Client.Auth;

/// <summary>
/// Sends no credential; relies on <c>X-Trino-User</c> alone. The default authenticator. See FR-2.2.1.
/// </summary>
public sealed class AnonymousAuthenticator : ITrinoAuthenticator
{
    /// <summary>The shared singleton instance.</summary>
    public static AnonymousAuthenticator Instance { get; } = new();

    /// <inheritdoc/>
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    /// <inheritdoc/>
    public void ConfigureHandler(SocketsHttpHandler handler)
    {
    }
}
