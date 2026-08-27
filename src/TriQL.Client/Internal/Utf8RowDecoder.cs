using System.Text.Json;
using TriQL.Client.Exceptions;
using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Decodes the <c>data</c> array of a statement response straight from the response's UTF-8 bytes
/// into final CLR values, driven by the column type signatures. See FR-7.2.6, NFR-PERF-3.
/// </summary>
/// <remarks>
/// This replaces the <see cref="RawJsonValueConverter"/> path, which built a <see cref="JsonElement"/>
/// tree, allocated an intermediate <see cref="string"/> for every text-shaped value (<c>decimal</c>,
/// <c>date</c>, <c>time</c>, <c>timestamp</c>, <c>uuid</c>, <c>varbinary</c>, <c>ipaddress</c>), boxed
/// each primitive, and only then parsed to the target type — roughly two allocations per value where
/// NFR-PERF-3 permits one.
/// <para>
/// Scalars are materialized eagerly here; <c>array</c>/<c>map</c>/<c>row</c> values are left as raw
/// structures and materialized on first access so FR-7.2.7's laziness is preserved.
/// </para>
/// </remarks>
internal static class Utf8RowDecoder
{
    /// <summary>
    /// The largest text-shaped scalar transcoded through a stack buffer. Trino's longest such value
    /// is a zoned picosecond timestamp (~55 chars); anything longer falls back to a heap string.
    /// </summary>
    private const int MaxStackChars = 128;

    /// <summary>Decodes an array-of-arrays <c>data</c> payload. <paramref name="json"/> must span exactly that array.</summary>
    public static object?[][] DecodeRows(ReadOnlySpan<byte> json, IReadOnlyList<TrinoColumn> columns)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new TrinoProtocolException("The 'data' member was expected to be an array of rows.");
        }

        // Cached so the per-row loop never re-parses a type string (FR-7.1.3).
        var types = new TrinoTypeSignature[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            types[i] = columns[i].TypeSignature;
        }

        var rows = new List<object?[]>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new TrinoProtocolException("Each entry of 'data' was expected to be an array of column values.");
            }

            rows.Add(DecodeRow(ref reader, types));
        }

        return [.. rows];
    }

    private static object?[] DecodeRow(ref Utf8JsonReader reader, TrinoTypeSignature[] types)
    {
        var values = new object?[types.Length];
        var ordinal = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            // A row wider than the declared schema is tolerated and its surplus discarded (FR-4.2 forward compatibility).
            if (ordinal < types.Length)
            {
                values[ordinal] = DecodeValue(ref reader, types[ordinal]);
            }
            else
            {
                reader.Skip();
            }

            ordinal++;
        }

        return values;
    }

    private static object? DecodeValue(ref Utf8JsonReader reader, TrinoTypeSignature type)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        try
        {
            return type.BaseName switch
            {
                "boolean" => reader.GetBoolean(),
                "tinyint" => reader.GetSByte(),
                "smallint" => reader.GetInt16(),
                "integer" => reader.GetInt32(),
                "bigint" => reader.GetInt64(),
                "real" => DecodeSingle(ref reader),
                "double" => DecodeDouble(ref reader),
                "varchar" or "char" or "json" => reader.GetString(),
                "varbinary" => reader.GetBytesFromBase64(),
                "uuid" => reader.GetGuid(),
                "decimal" or "date" or "time" or "timestamp" or "interval" or "ipaddress" => DecodeText(ref reader, type),
                "array" or "map" or "row" => DecodeRaw(ref reader),
                _ => DecodeRaw(ref reader),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw new TrinoTypeConversionException($"A value could not be converted to Trino type '{type.RawSignature}'.", ex);
        }
    }

    /// <summary>
    /// Parses a text-shaped scalar without allocating the intermediate string: the token's raw UTF-8
    /// is transcoded into a stack buffer, since every such Trino format is pure ASCII.
    /// </summary>
    private static object DecodeText(ref Utf8JsonReader reader, TrinoTypeSignature type)
    {
        Span<char> buffer = stackalloc char[MaxStackChars];
        var length = TryCopyAscii(ref reader, buffer);
        if (length >= 0)
        {
            return TrinoValueConverter.ParseScalarFromText(buffer[..length], type);
        }

        // Escaped, multi-segment, non-ASCII, or oversized — none legal for these formats, but never assume.
        var fallback = reader.GetString() ?? throw new TrinoProtocolException($"A '{type.RawSignature}' value was unexpectedly null.");
        return TrinoValueConverter.ParseScalarFromText(fallback.AsSpan(), type);
    }

    /// <summary>Copies an unescaped ASCII string token into <paramref name="buffer"/>, returning its length or -1 if unsuitable.</summary>
    private static int TryCopyAscii(scoped ref Utf8JsonReader reader, scoped Span<char> buffer)
    {
        if (reader.TokenType != JsonTokenType.String || reader.HasValueSequence || reader.ValueIsEscaped)
        {
            return -1;
        }

        var bytes = reader.ValueSpan;
        if (bytes.Length > buffer.Length)
        {
            return -1;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] > 0x7F)
            {
                return -1;
            }

            buffer[i] = (char)bytes[i];
        }

        return bytes.Length;
    }

    /// <summary>Trino sends non-finite floating-point values as JSON strings rather than numbers.</summary>
    private static double DecodeDouble(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? ParseNonFiniteDouble(ref reader) : reader.GetDouble();

    private static float DecodeSingle(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? (float)ParseNonFiniteDouble(ref reader) : reader.GetSingle();

    private static double ParseNonFiniteDouble(scoped ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("NaN"u8))
        {
            return double.NaN;
        }

        if (reader.ValueTextEquals("Infinity"u8))
        {
            return double.PositiveInfinity;
        }

        if (reader.ValueTextEquals("-Infinity"u8))
        {
            return double.NegativeInfinity;
        }

        Span<char> buffer = stackalloc char[MaxStackChars];
        var length = TryCopyAscii(ref reader, buffer);
        return length >= 0
            ? double.Parse(buffer[..length], System.Globalization.CultureInfo.InvariantCulture)
            : throw new FormatException("The value is not a valid floating-point literal.");
    }

    /// <summary>
    /// Builds the untyped structure for a value whose final materialization is deferred, mirroring
    /// <see cref="RawJsonValueConverter"/>'s shape so the same converters consume it later.
    /// </summary>
    private static object? DecodeRaw(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var integral) ? integral : reader.GetDouble();
            case JsonTokenType.StartArray:
                var items = new List<object?>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    items.Add(DecodeRaw(ref reader));
                }

                return items.ToArray();
            case JsonTokenType.StartObject:
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString()!;
                    reader.Read();
                    map[name] = DecodeRaw(ref reader);
                }

                return map;
            default:
                throw new TrinoProtocolException($"Unexpected JSON token '{reader.TokenType}' in row data.");
        }
    }
}
