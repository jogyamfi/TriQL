using System.Text.Json;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// Maps <see cref="StatementResponseDto"/> wire shapes into <see cref="TrinoPageEnvelope"/>. See FR-4.2.
/// </summary>
internal static class StatementResponseMapper
{
    public static TrinoPageEnvelope ToEnvelope(
        StatementResponseDto dto, ReadOnlySpan<byte> responseBytes, IReadOnlyList<TrinoColumn>? previousColumns, Uri? requestUri = null)
    {
        var columns = dto.Columns is { Count: > 0 }
            ? dto.Columns.ConvertAll(c => new TrinoColumn(c.Name, c.Type))
            : previousColumns;

        var (rows, valuesAreDecoded, pendingSpooling) = ExtractRowsOrPendingSpooling(dto.Data, responseBytes, columns);

        return new TrinoPageEnvelope(
            dto.Id,
            ResolveUri(dto.NextUri, requestUri, nameof(dto.NextUri)),
            ResolveUri(dto.PartialCancelUri, requestUri, nameof(dto.PartialCancelUri)),
            ResolveUri(dto.InfoUri, requestUri, nameof(dto.InfoUri)),
            columns,
            rows,
            valuesAreDecoded,
            dto.Stats is null ? null : ToStats(dto.Stats),
            dto.Error,
            dto.UpdateType,
            dto.UpdateCount,
            pendingSpooling);
    }

    // Coordinators behind a gateway or proxy can emit blank or relative URIs, so relative values are
    // resolved against the URI the response came from rather than rejected outright.
    private static Uri? ResolveUri(string? value, Uri? requestUri, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (requestUri is not null && Uri.TryCreate(requestUri, value, out var resolved))
        {
            return resolved;
        }

        throw new TrinoProtocolException($"The server returned '{value}' for '{memberName}', which is not a valid URI.");
    }

    /// <summary>
    /// Detects the direct-vs-spooled shape of the <c>data</c> member (FR-5.1.2). An array is decoded
    /// immediately (the existing direct-protocol path); an object is parsed into a
    /// <see cref="SpooledPageData"/> descriptor and handed back unresolved, since fetching spooled
    /// segments is async I/O that this synchronous mapper cannot perform — the caller
    /// (<see cref="StatementClient.ReadEnvelopeAsync"/>) resolves it via <see cref="SegmentClient"/>
    /// before the page reaches row decoding.
    /// </summary>
    private static (object?[][] Rows, bool ValuesAreDecoded, SpooledPageData? PendingSpooling) ExtractRowsOrPendingSpooling(
        RawJsonSlice? data, ReadOnlySpan<byte> responseBytes, IReadOnlyList<TrinoColumn>? columns)
    {
        if (data is not { } slice)
        {
            return ([], false, null);
        }

        if (slice.Kind == JsonValueKind.Object)
        {
            return ([], false, ParseSpooledEnvelope(responseBytes.Slice(slice.Start, slice.Length)));
        }

        if (slice.Kind != JsonValueKind.Array)
        {
            return ([], false, null);
        }

        var json = responseBytes.Slice(slice.Start, slice.Length);

        // Without a schema there is nothing to convert against, so fall back to untyped decoding.
        if (columns is not { Count: > 0 })
        {
            return (DecodeUntyped(json), false, null);
        }

        return (Utf8RowDecoder.DecodeRows(json, columns), true, null);
    }

    private static SpooledPageData ParseSpooledEnvelope(ReadOnlySpan<byte> json)
    {
        SpooledDataEnvelopeDto envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(json, TriqlInternalJsonContext.Default.SpooledDataEnvelopeDto)
                ?? throw new TrinoProtocolException("The spooled 'data' envelope was empty.");
        }
        catch (JsonException ex)
        {
            throw new TrinoProtocolException("The spooled 'data' envelope could not be parsed.", ex);
        }

        var segments = new SegmentDescriptor[envelope.Segments.Count];
        for (var i = 0; i < envelope.Segments.Count; i++)
        {
            segments[i] = ToSegmentDescriptor(envelope.Segments[i], i);
        }

        return new SpooledPageData(envelope.Encoding, segments);
    }

    private static SegmentDescriptor ToSegmentDescriptor(SpooledSegmentDto dto, int index)
    {
        var kind = dto.Type switch
        {
            "inline" => SegmentKind.Inline,
            "spooled" => SegmentKind.Spooled,
            _ => throw new TrinoProtocolException($"The server returned an unsupported spooled segment type '{dto.Type}' at index {index}."),
        };

        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers = dto.Headers is null
            ? null
            : dto.Headers.ToDictionary(kv => kv.Key, IReadOnlyList<string> (kv) => kv.Value, StringComparer.Ordinal);

        if (kind == SegmentKind.Inline)
        {
            if (dto.Data is null)
            {
                throw new TrinoProtocolException($"An inline spooled segment at index {index} is missing 'data'.");
            }

            return new SegmentDescriptor(
                kind, dto.Metadata.RowOffset, dto.Metadata.RowsCount, dto.Metadata.SegmentSize, dto.Metadata.UncompressedSize,
                dto.Data, SegmentUri: null, AckUri: null, headers);
        }

        if (string.IsNullOrEmpty(dto.Uri) || string.IsNullOrEmpty(dto.AckUri))
        {
            throw new TrinoProtocolException($"A spooled segment at index {index} is missing 'uri' or 'ackUri'.");
        }

        if (!Uri.TryCreate(dto.Uri, UriKind.Absolute, out var segmentUri))
        {
            throw new TrinoProtocolException($"A spooled segment at index {index} has an invalid 'uri'.");
        }

        if (!Uri.TryCreate(dto.AckUri, UriKind.Absolute, out var ackUri))
        {
            throw new TrinoProtocolException($"A spooled segment at index {index} has an invalid 'ackUri'.");
        }

        return new SegmentDescriptor(
            kind, dto.Metadata.RowOffset, dto.Metadata.RowsCount, dto.Metadata.SegmentSize, dto.Metadata.UncompressedSize,
            InlineDataBase64: null, segmentUri, ackUri, headers);
    }

    private static object?[][] DecodeUntyped(ReadOnlySpan<byte> json)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        var element = document.RootElement;
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
