using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// The parsed shape of a single <c>POST /v1/statement</c> or <c>nextUri</c> page response, before
/// any buffering or backoff decisions are applied.
/// </summary>
internal sealed record TrinoPageEnvelope(
    string QueryId,
    Uri? NextUri,
    Uri? PartialCancelUri,
    Uri? InfoUri,
    IReadOnlyList<TrinoColumn>? Columns,
    IReadOnlyList<object?[]> Rows,
    bool ValuesAreDecoded,
    TrinoQueryStats? Stats,
    StatementErrorDto? Error,
    string? UpdateType,
    long? UpdateCount);
