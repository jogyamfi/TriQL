using System.Text.Json;
using System.Text.Json.Serialization;

namespace TriQL.Client.Internal.Json;

/// <summary>Wire DTO for a single entry of the <c>columns</c> array. See FR-4.2.1.</summary>
internal sealed class StatementColumnDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>Wire DTO for <c>error.errorLocation</c>. See FR-12.1.2.</summary>
internal sealed class StatementErrorLocationDto
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("columnNumber")]
    public int ColumnNumber { get; set; }
}

/// <summary>Wire DTO for <c>error.failureInfo</c>. See FR-12.1.2.</summary>
internal sealed class StatementFailureInfoDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("suppressed")]
    public List<StatementFailureInfoDto>? Suppressed { get; set; }

    [JsonPropertyName("stack")]
    public List<string>? Stack { get; set; }
}

/// <summary>Wire DTO for the <c>error</c> member. See FR-4.2.1, FR-12.1.2.</summary>
internal sealed class StatementErrorDto
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("errorName")]
    public string ErrorName { get; set; } = string.Empty;

    [JsonPropertyName("errorType")]
    public string ErrorType { get; set; } = string.Empty;

    [JsonPropertyName("errorLocation")]
    public StatementErrorLocationDto? ErrorLocation { get; set; }

    [JsonPropertyName("failureInfo")]
    public StatementFailureInfoDto? FailureInfo { get; set; }
}

/// <summary>Wire DTO for a node of the <c>stats.rootStage</c> tree. See FR-11.3.3.</summary>
internal sealed class StatementStageStatsDto
{
    [JsonPropertyName("stageId")]
    public string? StageId { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("nodes")]
    public int Nodes { get; set; }

    [JsonPropertyName("totalSplits")]
    public int TotalSplits { get; set; }

    [JsonPropertyName("queuedSplits")]
    public int QueuedSplits { get; set; }

    [JsonPropertyName("runningSplits")]
    public int RunningSplits { get; set; }

    [JsonPropertyName("completedSplits")]
    public int CompletedSplits { get; set; }

    [JsonPropertyName("subStages")]
    public List<StatementStageStatsDto>? SubStages { get; set; }
}

/// <summary>Wire DTO for the <c>stats</c> member. See FR-11.3.3.</summary>
internal sealed class StatementStatsDto
{
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("queued")]
    public bool Queued { get; set; }

    [JsonPropertyName("scheduled")]
    public bool Scheduled { get; set; }

    [JsonPropertyName("nodes")]
    public int Nodes { get; set; }

    [JsonPropertyName("totalSplits")]
    public int TotalSplits { get; set; }

    [JsonPropertyName("queuedSplits")]
    public int QueuedSplits { get; set; }

    [JsonPropertyName("runningSplits")]
    public int RunningSplits { get; set; }

    [JsonPropertyName("completedSplits")]
    public int CompletedSplits { get; set; }

    [JsonPropertyName("cpuTimeMillis")]
    public long CpuTimeMillis { get; set; }

    [JsonPropertyName("wallTimeMillis")]
    public long WallTimeMillis { get; set; }

    [JsonPropertyName("queuedTimeMillis")]
    public long QueuedTimeMillis { get; set; }

    [JsonPropertyName("elapsedTimeMillis")]
    public long ElapsedTimeMillis { get; set; }

    [JsonPropertyName("processedRows")]
    public long ProcessedRows { get; set; }

    [JsonPropertyName("processedBytes")]
    public long ProcessedBytes { get; set; }

    [JsonPropertyName("physicalInputBytes")]
    public long PhysicalInputBytes { get; set; }

    [JsonPropertyName("peakMemoryBytes")]
    public long PeakMemoryBytes { get; set; }

    [JsonPropertyName("spilledBytes")]
    public long SpilledBytes { get; set; }

    [JsonPropertyName("progressPercentage")]
    public double? ProgressPercentage { get; set; }

    [JsonPropertyName("rootStage")]
    public StatementStageStatsDto? RootStage { get; set; }
}

/// <summary>
/// Wire DTO for the <c>POST /v1/statement</c> submission response and every subsequent
/// <c>nextUri</c> page. Unknown members are tolerated for forward compatibility. See FR-4.2.1.
/// </summary>
internal sealed class StatementResponseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("infoUri")]
    public string? InfoUri { get; set; }

    [JsonPropertyName("partialCancelUri")]
    public string? PartialCancelUri { get; set; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; set; }

    [JsonPropertyName("columns")]
    public List<StatementColumnDto>? Columns { get; set; }

    /// <summary>
    /// The <c>data</c> member, captured as a byte range rather than materialized. An array indicates
    /// the direct protocol (FR-5.1.2); an object indicates the spooled protocol, which is not
    /// implemented until Phase 5.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonConverter(typeof(RawJsonSliceConverter))]
    public RawJsonSlice? Data { get; set; }

    [JsonPropertyName("stats")]
    public StatementStatsDto? Stats { get; set; }

    [JsonPropertyName("error")]
    public StatementErrorDto? Error { get; set; }

    [JsonPropertyName("updateType")]
    public string? UpdateType { get; set; }

    [JsonPropertyName("updateCount")]
    public long? UpdateCount { get; set; }
}
