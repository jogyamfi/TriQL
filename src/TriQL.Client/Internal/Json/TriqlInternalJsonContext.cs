using System.Text.Json.Serialization;

namespace TriQL.Client.Internal.Json;

/// <summary>Wire DTO for the <c>nodeVersion</c> member of <c>GET /v1/info</c>.</summary>
internal sealed class NodeVersionDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>Wire DTO for the <c>GET /v1/info</c> response body.</summary>
internal sealed class ServerInfoDto
{
    [JsonPropertyName("nodeVersion")]
    public NodeVersionDto NodeVersion { get; set; } = new();

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    [JsonPropertyName("coordinator")]
    public bool Coordinator { get; set; }

    [JsonPropertyName("starting")]
    public bool Starting { get; set; }

    [JsonPropertyName("uptime")]
    public string? Uptime { get; set; }
}

/// <summary>
/// Source-generated serializer context for internal wire DTOs, kept trimming- and AOT-compatible (NFR-COMPAT-3, SEC-5).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 64)]
[JsonSerializable(typeof(ServerInfoDto))]
[JsonSerializable(typeof(StatementResponseDto))]
[JsonSerializable(typeof(QueryInfoDto))]
internal sealed partial class TriqlInternalJsonContext : JsonSerializerContext;
