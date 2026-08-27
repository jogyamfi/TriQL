using System.Globalization;
using System.Text;
using System.Text.Json;
using TriQL.Client.Exceptions;
using TriQL.Client.Internal;
using TriQL.Client.Internal.Json;
using TriQL.Client.Types;

namespace TriQL.Client.Tests.Types;

public sealed class Utf8RowDecoderTests
{
    private static object?[][] Decode(string dataJson, params string[] columnTypes)
    {
        var columns = new List<TrinoColumn>();
        for (var i = 0; i < columnTypes.Length; i++)
        {
            columns.Add(new TrinoColumn($"c{i}", columnTypes[i]));
        }

        return Utf8RowDecoder.DecodeRows(Encoding.UTF8.GetBytes(dataJson), columns);
    }

    private static object? DecodeSingleValue(string valueJson, string columnType) => Decode($"[[{valueJson}]]", columnType)[0][0];

    [Fact]
    public void Decode_Boolean()
    {
        Assert.Equal(true, DecodeSingleValue("true", "boolean"));
        Assert.Equal(false, DecodeSingleValue("false", "boolean"));
    }

    [Fact]
    public void Decode_Tinyint_YieldsSignedSByte()
    {
        Assert.Equal((sbyte)-100, DecodeSingleValue("-100", "tinyint"));
    }

    [Theory]
    [InlineData("smallint", typeof(short))]
    [InlineData("integer", typeof(int))]
    [InlineData("bigint", typeof(long))]
    public void Decode_IntegerTypes(string type, Type expected)
    {
        Assert.IsType(expected, DecodeSingleValue("42", type));
    }

    [Fact]
    public void Decode_RealAndDouble()
    {
        Assert.Equal(1.5f, DecodeSingleValue("1.5", "real"));
        Assert.Equal(1.5d, DecodeSingleValue("1.5", "double"));
    }

    [Theory]
    [InlineData("\"NaN\"", double.NaN)]
    [InlineData("\"Infinity\"", double.PositiveInfinity)]
    [InlineData("\"-Infinity\"", double.NegativeInfinity)]
    public void Decode_NonFiniteDouble_SentAsJsonString(string json, double expected)
    {
        Assert.Equal(expected, DecodeSingleValue(json, "double"));
    }

    [Fact]
    public void Decode_Decimal_WithinClrPrecision()
    {
        Assert.Equal(123.45m, DecodeSingleValue("\"123.45\"", "decimal(10,2)"));
    }

    [Fact]
    public void Decode_Decimal_BeyondClrPrecision()
    {
        var value = DecodeSingleValue("\"1234567890123456789012345678.1234567890\"", "decimal(38,10)");

        Assert.Equal("1234567890123456789012345678.1234567890", Assert.IsType<TrinoBigDecimal>(value).ToString());
    }

    [Fact]
    public void Decode_Varchar_PreservesNonAsciiAndEscapes()
    {
        Assert.Equal("naïve \"quoted\"", DecodeSingleValue("\"na\\u00EFve \\\"quoted\\\"\"", "varchar"));
    }

    [Fact]
    public void Decode_Char_PreservesPadding()
    {
        Assert.Equal("ab   ", DecodeSingleValue("\"ab   \"", "char(5)"));
    }

    [Fact]
    public void Decode_Varbinary_FromBase64()
    {
        Assert.Equal(new byte[] { 1, 2, 3 }, DecodeSingleValue($"\"{Convert.ToBase64String([1, 2, 3])}\"", "varbinary"));
    }

    [Fact]
    public void Decode_Uuid()
    {
        Assert.Equal(Guid.Parse("f7a2b8c0-1234-4567-8901-abcdefabcdef"), DecodeSingleValue("\"f7a2b8c0-1234-4567-8901-abcdefabcdef\"", "uuid"));
    }

    [Fact]
    public void Decode_Date()
    {
        Assert.Equal(new DateOnly(2001, 8, 22), DecodeSingleValue("\"2001-08-22\"", "date"));
    }

    [Fact]
    public void Decode_Timestamp_LowAndHighPrecision()
    {
        Assert.Equal(
            new DateTime(2001, 8, 22, 3, 4, 5, 321, DateTimeKind.Unspecified),
            DecodeSingleValue("\"2001-08-22 03:04:05.321\"", "timestamp(3)"));

        var high = DecodeSingleValue("\"2001-08-22 03:04:05.123456789012\"", "timestamp(12)");
        Assert.Equal("2001-08-22 03:04:05.123456789012", Assert.IsType<TrinoTimestamp>(high).ToString());
    }

    [Fact]
    public void Decode_TimestampWithNamedZone()
    {
        var value = DecodeSingleValue("\"2001-08-22 03:04:05.321 America/New_York\"", "timestamp(3) with time zone");

        Assert.Equal(TimeSpan.FromHours(-4), Assert.IsType<DateTimeOffset>(value).Offset);
    }

    [Fact]
    public void Decode_IpAddress()
    {
        Assert.Equal(System.Net.IPAddress.Parse("10.0.0.1"), DecodeSingleValue("\"10.0.0.1\"", "ipaddress"));
    }

    [Fact]
    public void Decode_IntervalYearToMonth()
    {
        Assert.Equal(new TrinoIntervalYearToMonth(14), DecodeSingleValue("\"1-2\"", "interval year to month"));
    }

    [Fact]
    public void Decode_Null_ForEveryTypeFamily()
    {
        var rows = Decode("[[null,null,null,null,null]]", "bigint", "varchar", "timestamp(3)", "array(bigint)", "row(a bigint)");

        Assert.All(rows[0], Assert.Null);
    }

    [Fact]
    public void Decode_ComplexTypes_AreLeftRawForDeferredMaterialization()
    {
        var rows = Decode("[[[1,2],{\"k\":\"v\"},[1,\"x\"]]]", "array(bigint)", "map(varchar,varchar)", "row(a bigint, b varchar)");

        // Still raw here; TrinoRow materializes them on first access (FR-7.2.7).
        Assert.IsType<object?[]>(rows[0][0]);
        Assert.IsType<Dictionary<string, object?>>(rows[0][1]);
        Assert.IsType<object?[]>(rows[0][2]);
    }

    [Fact]
    public void Decode_MultipleRows_PreservesOrder()
    {
        var rows = Decode("[[1,\"a\"],[2,\"b\"],[3,\"c\"]]", "bigint", "varchar");

        Assert.Equal(3, rows.Length);
        Assert.Equal(1L, rows[0][0]);
        Assert.Equal("c", rows[2][1]);
    }

    [Fact]
    public void Decode_EmptyDataArray_YieldsNoRows()
    {
        Assert.Empty(Decode("[]", "bigint"));
    }

    [Fact]
    public void Decode_RowWiderThanSchema_DiscardsSurplus()
    {
        var rows = Decode("[[1,\"extra\"]]", "bigint");

        Assert.Single(rows[0]);
        Assert.Equal(1L, rows[0][0]);
    }

    [Fact]
    public void Decode_MalformedValueForColumnType_RaisesTypeConversionException()
    {
        Assert.Throws<TrinoTypeConversionException>(() => Decode("[[\"not-a-date\"]]", "date"));
    }

    [Fact]
    public void Decode_MatchesTheNaiveConverterForEveryScalarType()
    {
        const string data = """
            [[true,-1,2,3,4,1.5,2.5,"12.34","hello","2001-08-22","03:04:05.321","2001-08-22 03:04:05.321","f7a2b8c0-1234-4567-8901-abcdefabcdef","10.0.0.1"]]
            """;
        string[] types =
        [
            "boolean", "tinyint", "smallint", "integer", "bigint", "real", "double",
            "decimal(10,2)", "varchar", "date", "time(3)", "timestamp(3)", "uuid", "ipaddress",
        ];

        var decoded = Decode(data, types)[0];

        using var document = JsonDocument.Parse(data);
        var naiveRaw = RawJsonValueConverter.ConvertRow(document.RootElement[0]);
        for (var i = 0; i < types.Length; i++)
        {
            var naive = TrinoValueConverter.Convert(naiveRaw[i], TrinoTypeSignature.Parse(types[i]));
            Assert.Equal(naive, decoded[i]);
        }
    }

    [Fact]
    public void RawJsonSliceOffsets_LocateDataWithinTheDeserializedBuffer()
    {
        // Guards the coupling that makes the decoder work: RawJsonSlice records byte offsets into the
        // very buffer JsonSerializer was handed, so the slice must be exactly the `data` array.
        const string body = """
            {"id":"q1","nextUri":"http://h/v1/statement/q1/2","columns":[{"name":"n","type":"bigint"}],"data":[[1],[2]],"stats":{"state":"RUNNING"}}
            """;
        var bytes = Encoding.UTF8.GetBytes(body);

        var dto = JsonSerializer.Deserialize(bytes, TriqlInternalJsonContext.Default.StatementResponseDto)!;
        var slice = dto.Data!.Value;

        Assert.Equal(JsonValueKind.Array, slice.Kind);
        Assert.Equal("[[1],[2]]", Encoding.UTF8.GetString(bytes, slice.Start, slice.Length));
    }

    [Fact]
    public void Mapper_DecodesRowsThroughTheRealDeserializationPath()
    {
        const string body = """
            {"id":"q1","columns":[{"name":"n","type":"tinyint"},{"name":"t","type":"timestamp(3)"}],"data":[[-5,"2001-08-22 03:04:05.321"]]}
            """;
        var bytes = Encoding.UTF8.GetBytes(body);
        var dto = JsonSerializer.Deserialize(bytes, TriqlInternalJsonContext.Default.StatementResponseDto)!;

        var envelope = StatementResponseMapper.ToEnvelope(dto, bytes, previousColumns: null);

        Assert.True(envelope.ValuesAreDecoded);
        Assert.Equal((sbyte)-5, envelope.Rows[0][0]);
        Assert.Equal(new DateTime(2001, 8, 22, 3, 4, 5, 321, DateTimeKind.Unspecified), envelope.Rows[0][1]);
    }

    [Fact]
    public void Mapper_WithoutColumns_FallsBackToUntypedDecodingAndFlagsValuesUndecoded()
    {
        const string body = """{"id":"q1","data":[[1,"x"]]}""";
        var bytes = Encoding.UTF8.GetBytes(body);
        var dto = JsonSerializer.Deserialize(bytes, TriqlInternalJsonContext.Default.StatementResponseDto)!;

        var envelope = StatementResponseMapper.ToEnvelope(dto, bytes, previousColumns: null);

        Assert.False(envelope.ValuesAreDecoded);
        Assert.Equal(1L, envelope.Rows[0][0]);
    }

    [Fact]
    public void Mapper_SpooledDataObject_StillRaisesProtocolException()
    {
        const string body = """{"id":"q1","data":{"encoding":"json+zstd","segments":[]}}""";
        var bytes = Encoding.UTF8.GetBytes(body);
        var dto = JsonSerializer.Deserialize(bytes, TriqlInternalJsonContext.Default.StatementResponseDto)!;

        Assert.Throws<TrinoProtocolException>(() => StatementResponseMapper.ToEnvelope(dto, bytes, previousColumns: null));
    }

    [Fact]
    public void DecodedRow_MaterializesComplexTypesLazilyAndReturnsScalarsDirectly()
    {
        List<TrinoColumn> columns = [new("n", "bigint"), new("a", "array(bigint)")];
        var values = Decode("[[7,[1,2]]]", "bigint", "array(bigint)")[0];

        var row = new TrinoRow(columns, values, valuesAreDecoded: true);

        Assert.Equal(7L, row.GetValue(0));
        Assert.Equal(new long[] { 1, 2 }, row.GetValue(1));
    }

    [Fact]
    public void DecodeRows_AllocatesNoMoreThanTheMaterializedValuesPlusASmallConstant()
    {
        // NFR-PERF-3. The eight materialized values below plus the row array total ~336 bytes; the
        // ceiling leaves headroom for runtime variation while still catching a regression to the
        // ~750 bytes/row of the JsonElement path, whose excess scaled per value rather than per row.
        const int allocationCeilingPerRow = 500;
        const int rowCount = 2_000;

        string[] types =
        [
            "bigint", "varchar", "double", "boolean",
            "decimal(10,2)", "date", "timestamp(3)", "uuid",
        ];
        var columns = new List<TrinoColumn>();
        for (var i = 0; i < types.Length; i++)
        {
            columns.Add(new TrinoColumn($"c{i}", types[i]));
        }

        var builder = new StringBuilder("[");
        for (var i = 0; i < rowCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CultureInfo.InvariantCulture, $$"""
                [{{i}},"customer#{{i:D9}}",{{i}}.5,true,"12.34","2001-08-22","2001-08-22 03:04:05.321","f7a2b8c0-1234-4567-8901-abcdefabcdef"]
                """);
        }

        var payload = Encoding.UTF8.GetBytes(builder.Append(']').ToString());

        // Warm up so JIT and first-call allocations are not attributed to the measured run.
        _ = Utf8RowDecoder.DecodeRows(payload, columns);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = Utf8RowDecoder.DecodeRows(payload, columns);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(rowCount, rows.Length);
        Assert.InRange(allocated / (double)rows.Length, 0, allocationCeilingPerRow);
    }
}
