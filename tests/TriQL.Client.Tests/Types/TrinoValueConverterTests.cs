using TriQL.Client.Exceptions;
using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Client.Tests.Types;

public sealed class TrinoValueConverterTests
{
    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        Assert.Null(TrinoValueConverter.Convert(null, TrinoTypeSignature.Parse("bigint")));
    }

    [Fact]
    public void Convert_Tinyint_MapsToSByteAndPreservesNegativeValues()
    {
        var value = TrinoValueConverter.Convert(-100L, TrinoTypeSignature.Parse("tinyint"));

        Assert.IsType<sbyte>(value);
        Assert.Equal((sbyte)-100, value);
    }

    [Fact]
    public void Convert_Tinyint_OverflowThrows()
    {
        // FR-7.2.4: narrowing that would lose data throws OverflowException directly, not wrapped.
        Assert.Throws<OverflowException>(() => TrinoValueConverter.Convert(200L, TrinoTypeSignature.Parse("tinyint")));
    }

    [Theory]
    [InlineData("smallint", typeof(short))]
    [InlineData("integer", typeof(int))]
    [InlineData("bigint", typeof(long))]
    public void Convert_IntegerTypes_MapToExpectedClrType(string typeName, Type expectedType)
    {
        var value = TrinoValueConverter.Convert(42L, TrinoTypeSignature.Parse(typeName));

        Assert.IsType(expectedType, value);
    }

    [Fact]
    public void Convert_Double_HandlesSpecialStringValues()
    {
        var type = TrinoTypeSignature.Parse("double");

        Assert.Equal(double.NaN, TrinoValueConverter.Convert("NaN", type));
        Assert.Equal(double.PositiveInfinity, TrinoValueConverter.Convert("Infinity", type));
        Assert.Equal(double.NegativeInfinity, TrinoValueConverter.Convert("-Infinity", type));
    }

    [Fact]
    public void Convert_DecimalWithinDecimalPrecision_ReturnsClrDecimal()
    {
        var value = TrinoValueConverter.Convert("123.4500", TrinoTypeSignature.Parse("decimal(10,4)"));

        Assert.IsType<decimal>(value);
        Assert.Equal(123.45m, value);
    }

    [Fact]
    public void Convert_DecimalBeyondDecimalPrecision_ReturnsTrinoBigDecimal()
    {
        var value = TrinoValueConverter.Convert("123456789012345678901234567890.1234567890", TrinoTypeSignature.Parse("decimal(38,10)"));

        var bigDecimal = Assert.IsType<TrinoBigDecimal>(value);
        Assert.Equal("123456789012345678901234567890.1234567890", bigDecimal.ToString());
    }

    [Fact]
    public void Convert_Varbinary_DecodesBase64()
    {
        var value = TrinoValueConverter.Convert(Convert.ToBase64String([1, 2, 3]), TrinoTypeSignature.Parse("varbinary"));

        Assert.Equal(new byte[] { 1, 2, 3 }, value);
    }

    [Fact]
    public void Convert_Date_ParsesDateOnly()
    {
        var value = TrinoValueConverter.Convert("2001-08-22", TrinoTypeSignature.Parse("date"));

        Assert.Equal(new DateOnly(2001, 8, 22), value);
    }

    [Fact]
    public void Convert_TimeWithinTimeOnlyPrecision_ReturnsTimeOnly()
    {
        var value = TrinoValueConverter.Convert("01:02:03.456", TrinoTypeSignature.Parse("time(3)"));

        Assert.Equal(new TimeOnly(1, 2, 3, 456), value);
    }

    [Fact]
    public void Convert_TimeBeyondTimeOnlyPrecision_ReturnsTrinoTime()
    {
        var value = TrinoValueConverter.Convert("01:02:03.123456789012", TrinoTypeSignature.Parse("time(12)"));

        var trinoTime = Assert.IsType<TrinoTime>(value);
        Assert.Equal("01:02:03.123456789012", trinoTime.ToString());
    }

    [Fact]
    public void Convert_TimestampWithinDateTimePrecision_ReturnsDateTimeWithUnspecifiedKind()
    {
        var value = TrinoValueConverter.Convert("2001-08-22 03:04:05.321", TrinoTypeSignature.Parse("timestamp(3)"));

        var dateTime = Assert.IsType<DateTime>(value);
        Assert.Equal(DateTimeKind.Unspecified, dateTime.Kind);
        Assert.Equal(new DateTime(2001, 8, 22, 3, 4, 5, 321, DateTimeKind.Unspecified), dateTime);
    }

    [Fact]
    public void Convert_TimestampWithTimeZoneOffset_ReturnsDateTimeOffset()
    {
        var value = TrinoValueConverter.Convert(
            "2001-08-22 03:04:05.321 +05:30", TrinoTypeSignature.Parse("timestamp(3) with time zone"));

        var offset = Assert.IsType<DateTimeOffset>(value);
        Assert.Equal(TimeSpan.FromHours(5.5), offset.Offset);
    }

    [Fact]
    public void Convert_IntervalYearToMonth_ParsesTotalMonths()
    {
        var value = TrinoValueConverter.Convert("1-2", TrinoTypeSignature.Parse("interval year to month"));

        var interval = Assert.IsType<TrinoIntervalYearToMonth>(value);
        Assert.Equal(14, interval.TotalMonths);
    }

    [Fact]
    public void Convert_IntervalDayToSecond_ReturnsTimeSpan()
    {
        var value = TrinoValueConverter.Convert("1 02:03:04.567", TrinoTypeSignature.Parse("interval day to second"));

        Assert.Equal(new TimeSpan(1, 2, 3, 4, 567), value);
    }

    [Fact]
    public void Convert_Uuid_ParsesGuid()
    {
        var value = TrinoValueConverter.Convert("f7a2b8c0-1234-4567-8901-abcdefabcdef", TrinoTypeSignature.Parse("uuid"));

        Assert.Equal(Guid.Parse("f7a2b8c0-1234-4567-8901-abcdefabcdef"), value);
    }

    [Fact]
    public void Convert_Array_ReturnsStronglyTypedArray()
    {
        var value = TrinoValueConverter.Convert(new object?[] { 1L, 2L, 3L }, TrinoTypeSignature.Parse("array(bigint)"));

        var array = Assert.IsType<long[]>(value);
        Assert.Equal([1L, 2L, 3L], array);
    }

    [Fact]
    public void Convert_ArrayWithNullElement_FallsBackToObjectArray()
    {
        var value = TrinoValueConverter.Convert(new object?[] { 1L, null }, TrinoTypeSignature.Parse("array(bigint)"));

        var array = Assert.IsType<object?[]>(value);
        Assert.Equal(1L, array[0]);
        Assert.Null(array[1]);
    }

    [Fact]
    public void Convert_Map_ConvertsKeysAndValues()
    {
        var raw = new Dictionary<string, object?> { ["1"] = "one", ["2"] = "two" };
        var value = TrinoValueConverter.Convert(raw, TrinoTypeSignature.Parse("map(integer, varchar)"));

        var dictionary = Assert.IsAssignableFrom<IReadOnlyDictionary<object, object?>>(value);
        Assert.Equal("one", dictionary[1]);
        Assert.Equal("two", dictionary[2]);
    }

    [Fact]
    public void Convert_Row_ExposesFieldsByNameAndOrdinal()
    {
        var raw = new object?[] { 1L, "ALGERIA" };
        var value = TrinoValueConverter.Convert(raw, TrinoTypeSignature.Parse("row(nationkey bigint, name varchar)"));

        var row = Assert.IsAssignableFrom<ITrinoRowValue>(value);
        Assert.Equal(1L, row[0]);
        Assert.Equal("ALGERIA", row["name"]);
        Assert.Equal(2, row.FieldCount);
    }
}
