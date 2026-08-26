using System.Net.Http.Headers;

namespace TriQL.Client.Auth;

/// <summary>
/// Extension point for authenticating requests to the coordinator. See FR-2.1.1.
/// </summary>
public interface ITrinoAuthenticator
{
    /// <summary>Validates configuration and acquires an initial credential if required.</summary>
    ValueTask InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Applies the credential to an outgoing request. Called for every request, including polls and cancellations (FR-2.1.2).</summary>
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked after a 401/403. Returns <see langword="true"/> if the credential was refreshed and the
    /// request should be retried exactly once (FR-2.1.3).
    /// </summary>
    ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken);

    /// <summary>Optional mutation of the HTTP handler, e.g. attaching client certificates.</summary>
    void ConfigureHandler(SocketsHttpHandler handler);
}

/// <summary>
/// Marker for authenticators that transmit a bearer-style credential (a header value that grants
/// access on its own), used to enforce the plaintext-credential guard (FR-1.1.3, SEC-3).
/// </summary>
internal interface ITransmitsBearerCredential;

/// <summary>Shared helpers for <see cref="ITrinoAuthenticator"/> implementations.</summary>
internal static class AuthenticatorHelpers
{
    public static void SetBearerToken(HttpRequestMessage request, string token) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static void SetBasicCredential(HttpRequestMessage request, string username, string password)
    {
        var raw = $"{username}:{password}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }
}
