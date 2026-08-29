using System.Net;
using System.Text.Json;
using TriQL.Client.Auth;

namespace TriQL.Client.Auth.Tests;

/// <summary>A minimal fake OAuth2 token endpoint scripted with a queue of responses.</summary>
internal sealed class StubTokenEndpointHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string? AccessToken, double? ExpiresIn)> _respond;

    public int RequestCount { get; private set; }
    public List<string> CapturedBodies { get; } = [];

    public StubTokenEndpointHandler(Func<HttpRequestMessage, (HttpStatusCode, string?, double?)> respond) => _respond = respond;

    public static StubTokenEndpointHandler AlwaysReturning(string accessToken, double expiresIn) =>
        new(_ => (HttpStatusCode.OK, accessToken, expiresIn));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (request.Content is not null)
        {
            CapturedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        var (status, accessToken, expiresIn) = _respond(request);
        var response = new HttpResponseMessage(status);
        if (accessToken is not null)
        {
            var json = JsonSerializer.Serialize(new { access_token = accessToken, expires_in = expiresIn, token_type = "Bearer" });
            response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        return response;
    }
}
