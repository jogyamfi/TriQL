using Azure.Core;
using Azure.Identity;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Auth;

/// <summary>
/// Sends <c>Authorization: Bearer &lt;token&gt;</c> using a <see cref="TokenCredential"/> for
/// Microsoft Entra ID, defaulting to <see cref="DefaultAzureCredential"/>. Accepts an explicit
/// credential for testability and for managed identity / workload identity scenarios. Caches the
/// acquired token until it is within the refresh skew of expiring and refreshes on 401, with
/// single-flight refresh under concurrency. See FR-2.3.2.
/// </summary>
public sealed class EntraIdAuthenticator : ITrinoAuthenticator, ITransmitsBearerCredential, IDisposable
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;
    private readonly TimeSpan _refreshSkew;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // A single immutable snapshot published by one atomic reference write. AccessToken? is a
    // multi-field nullable struct, so assigning it while ApplyAsync reads it outside the refresh
    // lock would be a torn read.
    private volatile TokenSnapshot? _snapshot;

    /// <summary>Initializes a new instance of the <see cref="EntraIdAuthenticator"/> class.</summary>
    /// <param name="scopes">
    /// The <see cref="TokenRequestContext"/> scopes to request, e.g. a resource's <c>.default</c> scope.
    /// </param>
    /// <param name="credential">
    /// The credential used to acquire tokens. Defaults to <see cref="DefaultAzureCredential"/>, which
    /// supports managed identity and workload identity automatically; supply an explicit credential
    /// (e.g. a fake for tests, or <c>ClientSecretCredential</c>) to override.
    /// </param>
    /// <param name="refreshSkew">The refresh skew applied to the token's expiry. Defaults to 60 seconds.</param>
    public EntraIdAuthenticator(IEnumerable<string> scopes, TokenCredential? credential = null, TimeSpan? refreshSkew = null)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = [.. scopes];
        if (_scopes.Length == 0)
        {
            throw new ArgumentException("At least one scope is required.", nameof(scopes));
        }

        _credential = credential ?? new DefaultAzureCredential();
        _refreshSkew = refreshSkew ?? TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc/>
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatorHelpers.SetBearerToken(request, token);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public void ConfigureHandler(SocketsHttpHandler handler)
    {
    }

    /// <inheritdoc/>
    public void Dispose() => _refreshLock.Dispose();

    private async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsNearExpiry())
        {
            await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
        }

        return _snapshot?.Token ?? throw new TrinoAuthenticationException("No Entra ID token is available.");
    }

    private bool IsNearExpiry()
    {
        var snapshot = _snapshot;
        return snapshot is null || DateTimeOffset.UtcNow >= snapshot.ExpiresOn - _refreshSkew;
    }

    private async ValueTask RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another waiter may have already refreshed while this call was blocked on the lock.
            if (!force && !IsNearExpiry())
            {
                return;
            }

            var context = new TokenRequestContext(_scopes);
            try
            {
                var token = await _credential.GetTokenAsync(context, cancellationToken).ConfigureAwait(false);
                _snapshot = new TokenSnapshot(token.Token, token.ExpiresOn);
            }
            catch (AuthenticationFailedException ex)
            {
                throw new TrinoAuthenticationException("Failed to acquire an Entra ID token.", ex);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed record TokenSnapshot(string Token, DateTimeOffset ExpiresOn);
}
