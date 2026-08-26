using System.Text.Json;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Auth;

/// <summary>
/// Sends <c>Authorization: Bearer &lt;token&gt;</c>. Supports a static token or a refresh callback,
/// refreshing on 401 and proactively when a known <c>exp</c> claim is within the configured skew. See FR-2.2.4.
/// </summary>
public sealed class JwtAuthenticator : ITrinoAuthenticator, ITransmitsBearerCredential, IDisposable
{
    private readonly Func<CancellationToken, ValueTask<string>>? _refreshCallback;
    private readonly TimeSpan _refreshSkew;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _currentToken;
    private DateTimeOffset? _currentExpiry;

    /// <summary>Initializes a new instance using a fixed, non-refreshing token.</summary>
    public JwtAuthenticator(string staticToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(staticToken);

        _currentToken = staticToken;
        _currentExpiry = TryReadExpiry(staticToken);
        _refreshSkew = TimeSpan.FromSeconds(60);
    }

    /// <summary>Initializes a new instance backed by a refresh callback invoked on demand.</summary>
    public JwtAuthenticator(Func<CancellationToken, ValueTask<string>> refreshCallback, TimeSpan? refreshSkew = null)
    {
        ArgumentNullException.ThrowIfNull(refreshCallback);

        _refreshCallback = refreshCallback;
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
        if (_refreshCallback is null)
        {
            return false;
        }

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
        if (_refreshCallback is not null && IsNearExpiry())
        {
            await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
        }

        return _currentToken ?? throw new TrinoAuthenticationException("No JWT token is available.");
    }

    private bool IsNearExpiry()
    {
        if (_currentToken is null)
        {
            return true;
        }

        return _currentExpiry is { } expiry && DateTimeOffset.UtcNow >= expiry - _refreshSkew;
    }

    private async ValueTask RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        if (_refreshCallback is null)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another waiter may have already refreshed while this call was blocked on the lock.
            if (!force && !IsNearExpiry())
            {
                return;
            }

            var token = await _refreshCallback(cancellationToken).ConfigureAwait(false);
            _currentToken = token;
            _currentExpiry = TryReadExpiry(token);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static DateTimeOffset? TryReadExpiry(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(PadBase64Url(parts[1]));
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("exp", out var expElement) && expElement.TryGetInt64(out var expSeconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }

        return null;
    }

    private static string PadBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (normalized.Length % 4)) % 4;
        return normalized.PadRight(normalized.Length + padding, '=');
    }
}
