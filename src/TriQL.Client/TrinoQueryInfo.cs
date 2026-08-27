using TriQL.Client.Exceptions;

namespace TriQL.Client;

/// <summary>
/// A snapshot of <c>GET /v1/query/{queryId}</c> statistics. A distinct, Duration-string-based
/// shape from <see cref="TrinoQueryStats"/>, which mirrors the millis-based <c>stats</c> member
/// carried on <c>/v1/statement</c> pages. See FR-10.3.
/// </summary>
public sealed record TrinoQueryInfoStats(
    string? State,
    bool Queued,
    bool Scheduled,
    string? ElapsedTime,
    string? QueuedTime,
    string? TotalCpuTime,
    long ProcessedInputPositions,
    string? ProcessedInputDataSize,
    string? PeakUserMemoryReservation);

/// <summary>
/// The response of <c>GET /v1/query/{queryId}</c>, exposed via <see cref="TrinoClient.GetQueryInfoAsync"/>. See FR-10.3.
/// </summary>
public sealed record TrinoQueryInfo(
    string QueryId,
    string State,
    string Query,
    string? SessionUser,
    string? SessionCatalog,
    string? SessionSchema,
    TrinoQueryInfoStats? QueryStats,
    TrinoFailureInfo? FailureInfo);
