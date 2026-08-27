using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TriQL.Client.Auth;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// <c>GET /v1/query/{queryId}</c>. See FR-10.3, FR-10.6.
/// </summary>
internal static class QueryInfoClient
{
    private const int MaxDiagnosticBodyBytes = 8192;

    public static async Task<TrinoQueryInfo> GetQueryInfoAsync(
        HttpMessageInvoker invoker,
        TrinoSessionOptions options,
        string queryId,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(options.Server!, $"v1/query/{Uri.EscapeDataString(queryId)}");

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

        QueryInfoDto dto;
        try
        {
            dto = await response.Content.ReadFromJsonAsync(TriqlInternalJsonContext.Default.QueryInfoDto, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new TrinoProtocolException($"The /v1/query/{queryId} response body was empty.");
        }
        catch (JsonException ex)
        {
            throw new TrinoProtocolException($"The /v1/query/{queryId} response could not be parsed.", ex, queryId);
        }

        return new TrinoQueryInfo(
            dto.QueryId,
            dto.State,
            dto.Query,
            dto.Session?.User,
            dto.Session?.Catalog,
            dto.Session?.Schema,
            dto.QueryStats is { } stats
                ? new TrinoQueryInfoStats(
                    stats.State,
                    stats.Queued,
                    stats.Scheduled,
                    stats.ElapsedTime,
                    stats.QueuedTime,
                    stats.TotalCpuTime,
                    stats.ProcessedInputPositions,
                    stats.ProcessedInputDataSize,
                    stats.PeakUserMemoryReservation)
                : null,
            FailureClassifier.ToFailureInfo(dto.FailureInfo));
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Length > MaxDiagnosticBodyBytes ? body[..MaxDiagnosticBodyBytes] : body;
    }
}
