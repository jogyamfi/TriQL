using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// <c>GET /v1/info</c>. See FR-10.1, FR-10.6.
/// </summary>
internal static class InfoClient
{
    private const int MaxDiagnosticBodyBytes = 8192;

    public static async Task<TrinoServerInfo> GetServerInfoAsync(
        HttpMessageInvoker invoker,
        TrinoSessionOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(options.Server!, "v1/info");

        using var response = await RequestExecutor.SendAsync(
            invoker,
            () => new HttpRequestMessage(HttpMethod.Get, requestUri),
            options.Authenticator ?? AnonymousAuthenticator.Instance,
            options,
            logger,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await ReadTruncatedBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw new TrinoConnectionException($"GET {requestUri} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        ServerInfoDto dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync(TriqlInternalJsonContext.Default.ServerInfoDto, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new TrinoProtocolException("The /v1/info response body was empty.");
        }
        catch (JsonException ex)
        {
            throw new TrinoProtocolException("The /v1/info response could not be parsed.", ex, queryId: null);
        }

        var info = new TrinoServerInfo(dto.NodeVersion.Version, dto.Environment, dto.Coordinator, dto.Starting, dto.Uptime);
        return info;
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxDiagnosticBodyBytes ? body[..MaxDiagnosticBodyBytes] : body;
    }
}
