using System.Text.Json.Serialization;

namespace TriQL.Client.Internal.Json;

/// <summary>
/// Wire DTO for a spooled segment's <c>metadata</c> object. <c>rowsCount</c> and
/// <c>uncompressedSize</c> are optional: Trino did not enforce <c>rowsCount</c> as mandatory on the
/// response before server release 475 (a floor-466 server can omit it), and
/// <c>uncompressedSize</c> is absent whenever the server left a segment uncompressed — see FR-5.2.1.
/// </summary>
internal sealed class SpooledSegmentMetadataDto
{
    [JsonPropertyName("rowOffset")]
    public long RowOffset { get; set; }

    [JsonPropertyName("rowsCount")]
    public long? RowsCount { get; set; }

    [JsonPropertyName("segmentSize")]
    public long SegmentSize { get; set; }

    [JsonPropertyName("uncompressedSize")]
    public long? UncompressedSize { get; set; }
}

/// <summary>
/// Wire DTO for one entry of the spooled <c>data.segments</c> array. <c>data</c>/<c>uri</c>/<c>ackUri</c>/<c>headers</c>
/// are populated according to <c>type</c> (<c>"inline"</c> or <c>"spooled"</c>). See FR-5.2.1.
/// </summary>
internal sealed class SpooledSegmentDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public SpooledSegmentMetadataDto Metadata { get; set; } = new();

    /// <summary>The base64 payload, present only for an <c>"inline"</c> segment.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>The URI to fetch the payload from, present only for a <c>"spooled"</c> segment.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    /// <summary>The URI to acknowledge consumption to, present only for a <c>"spooled"</c> segment.</summary>
    [JsonPropertyName("ackUri")]
    public string? AckUri { get; set; }

    /// <summary>Extra headers to attach when fetching <see cref="Uri"/>/<see cref="AckUri"/>, present only for a <c>"spooled"</c> segment.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, List<string>>? Headers { get; set; }
}

/// <summary>
/// Wire DTO for the spooled-protocol <c>data</c> member shape: <c>{ "encoding": "...", "segments": [...] }</c>.
/// Detected by <see cref="RawJsonSlice.Kind"/> being <see cref="System.Text.Json.JsonValueKind.Object"/>. See FR-5.1.2.
/// </summary>
internal sealed class SpooledDataEnvelopeDto
{
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = string.Empty;

    [JsonPropertyName("segments")]
    public List<SpooledSegmentDto> Segments { get; set; } = [];
}
