using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TriQL.Client.Exceptions;

namespace TriQL.Client.Auth;

/// <summary>
/// Sends <c>Authorization: Bearer &lt;token&gt;</c> using a token acquired via the RFC 6749 §4.4
/// client-credentials grant. Caches the token until <c>expires_in</c> minus a configurable skew
/// (default 60 s) and refreshes on 401, with single-flight refresh under concurrency. See FR-2.3.1.
/// </summary>
public sealed class OAuth2ClientCredentialsAuthenticator : ITrinoAuthenticator, ITransmitsBearerCredential, IDisposable
{
    private readonly Uri _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string[] _scopes;
    private readonly string? _audience;
    private readonly TimeSpan _refreshSkew;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // A single immutable snapshot published by one atomic reference write. Separate token/expiry
    // fields would let a concurrent reader see a torn DateTimeOffset or a mismatched pair, since
    // ApplyAsync reads them outside the refresh lock.
    private volatile TokenSnapshot? _snapshot;

    /// <summary>Initializes a new instance of the <see cref="OAuth2ClientCredentialsAuthenticator"/> class.</summary>
    /// <param name="tokenEndpoint">The OAuth 2.0 token endpoint.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="clientSecret">The client secret.</param>
    /// <param name="scopes">The requested scopes, space-joined in the token request. May be empty.</param>
    /// <param name="audience">An optional <c>audience</c> parameter, honored by some authorization servers.</param>
    /// <param name="refreshSkew">The refresh skew applied to <c>expires_in</c>. Defaults to 60 seconds.</param>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> used to call <paramref name="tokenEndpoint"/>. When <see langword="null"/>, an
    /// owned instance is created and disposed with this authenticator; supply one (e.g. against a stub token
    /// endpoint) for testability.
    /// </param>
    public OAuth2ClientCredentialsAuthenticator(
        Uri tokenEndpoint,
        string clientId,
        string clientSecret,
        IEnumerable<string>? scopes = null,
        string? audience = null,
        TimeSpan? refreshSkew = null,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(tokenEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);

        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _scopes = scopes?.ToArray() ?? [];
        _audience = audience;
        _refreshSkew = refreshSkew ?? TimeSpan.FromSeconds(60);

        if (httpClient is null)
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
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
    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsNearExpiry())
        {
            await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
        }

        return _snapshot?.Token ?? throw new TrinoAuthenticationException("No OAuth2 access token is available.");
    }

    private bool IsNearExpiry()
    {
        var snapshot = _snapshot;
        return snapshot is null || (snapshot.Expiry is { } expiry && DateTimeOffset.UtcNow >= expiry - _refreshSkew);
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

            var (token, expiresIn) = await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
            _snapshot = new TokenSnapshot(token, expiresIn is { } seconds ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private sealed record TokenSnapshot(string Token, DateTimeOffset? Expiry);

    private async Task<(string Token, double? ExpiresIn)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string?, string?>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", _clientId),
            new("client_secret", _clientSecret),
        };

        if (_scopes.Length > 0)
        {
            form.Add(new("scope", string.Join(' ', _scopes)));
        }

        if (_audience is { Length: > 0 })
        {
            form.Add(new("audience", _audience));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint) { Content = new FormUrlEncodedContent(form) };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new TrinoAuthenticationException($"Failed to reach the OAuth2 token endpoint '{_tokenEndpoint}'.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TrinoAuthenticationException(
                    $"The OAuth2 token endpoint '{_tokenEndpoint}' returned status {(int)response.StatusCode}.");
            }

            OAuth2TokenResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync(OAuth2TokenResponseJsonContext.Default.OAuth2TokenResponse, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new TrinoAuthenticationException("The OAuth2 token endpoint returned a response that could not be parsed.", ex);
            }

            if (payload?.AccessToken is not { Length: > 0 })
            {
                throw new TrinoAuthenticationException("The OAuth2 token endpoint response did not contain an 'access_token'.");
            }

            return (payload.AccessToken, payload.ExpiresIn);
        }
    }
}

internal sealed class OAuth2TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public double? ExpiresIn { get; set; }
}

[JsonSerializable(typeof(OAuth2TokenResponse))]
internal sealed partial class OAuth2TokenResponseJsonContext : JsonSerializerContext;
