using System.Text.Json;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// Maps <see cref="StatementResponseDto"/> wire shapes into <see cref="TrinoPageEnvelope"/>. See FR-4.2.
/// </summary>
internal static class StatementResponseMapper
{
    public static TrinoPageEnvelope ToEnvelope(StatementResponseDto dto, IReadOnlyList<TrinoColumn>? previousColumns)
    {
        var columns = dto.Columns is { Count: > 0 }
            ? dto.Columns.ConvertAll(c => new TrinoColumn(c.Name, c.Type))
            : previousColumns;

        return new TrinoPageEnvelope(
            dto.Id,
            dto.NextUri is null ? null : new Uri(dto.NextUri),
            dto.PartialCancelUri is null ? null : new Uri(dto.PartialCancelUri),
            columns,
            ExtractRows(dto.Data),
            dto.Stats is null ? null : ToStats(dto.Stats),
            dto.Error,
            dto.UpdateType,
            dto.UpdateCount);
    }

    private static object?[][] ExtractRows(JsonElement? data)
    {
        if (data is not { } element)
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            // FR-5.1.2: an object `data` member indicates the spooled protocol, which Phase 5 implements.
            throw new TrinoProtocolException(
                "The server returned spooled-protocol response data, which is not supported until Phase 5. " +
                "Set TrinoSessionOptions.QueryDataEncodings to an empty list to force the direct protocol.");
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new object?[element.GetArrayLength()][];
        var i = 0;
        foreach (var rowElement in element.EnumerateArray())
        {
            rows[i++] = RawJsonValueConverter.ConvertRow(rowElement);
        }

        return rows;
    }

    private static TrinoQueryStats ToStats(StatementStatsDto dto) => new(
        dto.State,
        dto.Queued,
        dto.Scheduled,
        dto.Nodes,
        dto.TotalSplits,
        dto.QueuedSplits,
        dto.RunningSplits,
        dto.CompletedSplits,
        TimeSpan.FromMilliseconds(dto.CpuTimeMillis),
        TimeSpan.FromMilliseconds(dto.WallTimeMillis),
        TimeSpan.FromMilliseconds(dto.QueuedTimeMillis),
        TimeSpan.FromMilliseconds(dto.ElapsedTimeMillis),
        dto.ProcessedRows,
        dto.ProcessedBytes,
        dto.PhysicalInputBytes,
        dto.PeakMemoryBytes,
        dto.SpilledBytes,
        dto.ProgressPercentage,
        ToStage(dto.RootStage));

    private static TrinoStageStats? ToStage(StatementStageStatsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var subStages = new List<TrinoStageStats>();
        if (dto.SubStages is not null)
        {
            foreach (var sub in dto.SubStages)
            {
                if (ToStage(sub) is { } converted)
                {
                    subStages.Add(converted);
                }
            }
        }

        return new TrinoStageStats(
            dto.StageId, dto.State, dto.Done, dto.Nodes, dto.TotalSplits, dto.QueuedSplits, dto.RunningSplits, dto.CompletedSplits, subStages);
    }
}
