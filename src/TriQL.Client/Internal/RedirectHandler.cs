namespace TriQL.Client.Internal;

/// <summary>
/// Manually follows redirects for <c>GET</c> requests only, up to a configurable maximum, and never
/// across an <c>https</c>→<c>http</c> scheme downgrade. See FR-3.3.5.
/// </summary>
/// <remarks>
/// The underlying <see cref="SocketsHttpHandler"/> is configured with
/// <see cref="HttpClientHandler.AllowAutoRedirect"/>-equivalent disabled so this handler has sole
/// control over redirect behaviour.
/// </remarks>
internal sealed class RedirectHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public const int MaxRedirects = 5;

    private static readonly HashSet<int> RedirectStatusCodes = [301, 302, 303, 307, 308];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var currentRequest = request;
        var ownsCurrentRequest = false;
        var redirectCount = 0;

        while (true)
        {
            var response = await base.SendAsync(currentRequest, cancellationToken).ConfigureAwait(false);
            var redirectUri = TryGetRedirectUri(currentRequest, response, redirectCount);

            if (ownsCurrentRequest)
            {
                currentRequest.Dispose();
            }

            if (redirectUri is null)
            {
                return response;
            }

            response.Dispose();
            redirectCount++;
#pragma warning disable CA2000 // Disposed at the top of the next iteration (ownsCurrentRequest) or by the final iteration above; the analyzer cannot trace disposal across loop iterations.
            currentRequest = new HttpRequestMessage(HttpMethod.Get, redirectUri);
#pragma warning restore CA2000
            ownsCurrentRequest = true;
        }
    }

    private static Uri? TryGetRedirectUri(HttpRequestMessage currentRequest, HttpResponseMessage response, int redirectCount)
    {
        if (!RedirectStatusCodes.Contains((int)response.StatusCode)
            || currentRequest.Method != HttpMethod.Get
            || response.Headers.Location is not { } location
            || redirectCount >= MaxRedirects)
        {
            return null;
        }

        var redirectUri = location.IsAbsoluteUri ? location : new Uri(currentRequest.RequestUri!, location);

        var isDowngrade = string.Equals(currentRequest.RequestUri!.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

        return isDowngrade ? null : redirectUri;
    }
}
