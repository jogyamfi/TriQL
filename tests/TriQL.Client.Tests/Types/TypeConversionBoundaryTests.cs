using System.Text.Json;
using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Client.Tests.Types;

/// <summary>
/// Regression coverage for the FR-7.2 boundary cases and defects found during the Phase 3 cross-check.
/// </summary>
public sealed class TypeConversionBoundaryTests
{
    [Theory]
    [InlineData("America/New_York")]
    [InlineData("Europe/London")]
    [InlineData("UTC")]
    public void TimestampWithTimeZone_NamedZone_ResolvesWithoutOverflow(string zoneId)
    {
        var value = TrinoValueConverter.Convert(
            $"2001-08-22 23:59:59.321 {zoneId}", TrinoTypeSignature.Parse("timestamp(3) with time zone"));

        var offset = Assert.IsType<DateTimeOffset>(value);
        Assert.Equal(new DateTime(2001, 8, 22, 23, 59, 59, 321), offset.DateTime);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(zoneId).GetUtcOffset(offset.DateTime), offset.Offset);
    }

    [Fact]
    public void TimestampWithTimeZone_NamedZone_DoesNotApplyHostLocalZone()
    {
        // FR-7.2.5: the wire value's zone governs, never the host machine's local zone.
        var value = (DateTimeOffset)TrinoValueConverter.Convert(
            "2001-01-15 12:00:00.000 Asia/Kolkata", TrinoTypeSignature.Parse("timestamp(3) with time zone"))!;

        Assert.Equal(TimeSpan.FromMinutes(330), value.Offset);
        Assert.NotEqual(TimeZoneInfo.Local.GetUtcOffset(value.DateTime), value.Offset);
    }

    [Fact]
    public void TimestampWithTimeZone_HighPrecision_KeepsPicosecondsAndNamedZone()
    {
        var value = TrinoValueConverter.Convert(
            "2001-08-22 23:59:59.123456789012 America/New_York", TrinoTypeSignature.Parse("timestamp(12) with time zone"));

        var zoned = Assert.IsType<TrinoTimestampWithTimeZone>(value);
        Assert.Equal("America/New_York", zoned.ZoneId);
        Assert.Equal(86_399_123_456_789_012L, zoned.PicosecondOfDay);
    }

    [Theory]
    [InlineData("timestamp(7)", typeof(DateTime))]
    [InlineData("timestamp(8)", typeof(TrinoTimestamp))]
    [InlineData("timestamp(12)", typeof(TrinoTimestamp))]
    [InlineData("time(7)", typeof(TimeOnly))]
    [InlineData("time(8)", typeof(TrinoTime))]
    [InlineData("time(12)", typeof(TrinoTime))]
    public void TemporalPrecisionBoundary_SelectsExpectedClrType(string typeName, Type expected)
    {
        Assert.Equal(expected, TrinoValueConverter.GetDefaultClrType(TrinoTypeSignature.Parse(typeName)));
    }

    [Fact]
    public void Timestamp_Precision12_RetainsPicosecondPrecision()
    {
        var value = TrinoValueConverter.Convert("2001-08-22 03:04:05.123456789012", TrinoTypeSignature.Parse("timestamp(12)"));

        var timestamp = Assert.IsType<TrinoTimestamp>(value);
        Assert.Equal("2001-08-22 03:04:05.123456789012", timestamp.ToString());
    }

    [Theory]
    [InlineData("decimal(28,2)", typeof(decimal))]
    [InlineData("decimal(29,2)", typeof(TrinoBigDecimal))]
    [InlineData("decimal(38,10)", typeof(TrinoBigDecimal))]
    [InlineData("decimal", typeof(TrinoBigDecimal))]
    public void DecimalPrecisionBoundary_SelectsExpectedClrType(string typeName, Type expected)
    {
        Assert.Equal(expected, TrinoValueConverter.GetDefaultClrType(TrinoTypeSignature.Parse(typeName)));
    }

    [Theory]
    [InlineData("decimal(28,2)", "1.50")]
    [InlineData("decimal(38,10)", "1.5000000000")]
    [InlineData("decimal", "2")]
    public void GetDefaultClrType_AlwaysAgreesWithTheMaterializedValue(string typeName, string wireValue)
    {
        var type = TrinoTypeSignature.Parse(typeName);

        var declared = TrinoValueConverter.GetDefaultClrType(type);
        var actual = TrinoValueConverter.Convert(wireValue, type)!.GetType();

        Assert.Equal(declared, actual);
    }

    [Fact]
    public void Decimal_Precision38_RetainsFullPrecision()
    {
        const string wire = "1234567890123456789012345678.1234567890";
        var value = TrinoValueConverter.Convert(wire, TrinoTypeSignature.Parse("decimal(38,10)"));

        Assert.Equal(wire, Assert.IsType<TrinoBigDecimal>(value).ToString());
    }

    [Fact]
    public void Char_PreservesServerSidePadding()
    {
        var value = TrinoValueConverter.Convert("ab   ", TrinoTypeSignature.Parse("char(5)"));

        Assert.Equal("ab   ", value);
    }

    [Fact]
    public void UnknownColumnType_MapsToObject()
    {
        Assert.Equal(typeof(object), TrinoValueConverter.GetDefaultClrType(TrinoTypeSignature.Parse("unknown")));
    }

    [Fact]
    public void ArrayOfReferenceType_WithNullElement_KeepsDeclaredArrayType()
    {
        var type = TrinoTypeSignature.Parse("array(varchar)");

        var declared = TrinoValueConverter.GetDefaultClrType(type);
        var value = TrinoValueConverter.Convert(new object?[] { "a", null }, type);

        Assert.Equal(declared, value!.GetType());
        var array = Assert.IsType<string[]>(value);
        Assert.Equal("a", array[0]);
        Assert.Null(array[1]);
    }

    [Fact]
    public void ArrayOfValueType_WithNullElement_FallsBackToObjectArray()
    {
        var value = TrinoValueConverter.Convert(new object?[] { 1L, null }, TrinoTypeSignature.Parse("array(bigint)"));

        Assert.IsType<object?[]>(value);
    }

    [Fact]
    public void GetFieldValue_JsonDocument_IsSupportedOnJsonColumns()
    {
        var row = new TrinoRow([new TrinoColumn("doc", "json")], ["{\"a\":1}"]);

        using var document = row.GetFieldValue<JsonDocument>(0);

        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
    }

    [Fact]
    public void GetFieldValue_OnNullForNonNullableValueType_Throws()
    {
        var row = new TrinoRow([new TrinoColumn("id", "bigint")], [null]);

        Assert.Throws<InvalidCastException>(() => row.GetFieldValue<long>(0));
    }
}
