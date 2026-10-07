namespace TriQL.Client.Tests.Streaming;

/// <summary>
/// EF0-T3: <see cref="TrinoRow.GetFieldValue{T}"/> converts when the column's default CLR type
/// differs from the requested one, as EF Core's materializers require.
/// </summary>
public sealed class TrinoRowFieldValueConversionTests
{
    private static TrinoRow Row(string type, object? raw) => new([new TrinoColumn("c", type)], [raw]);

    private enum Color : short
    {
        Red = 1,
        Green = 2,
    }

    [Fact]
    public void IntegerColumn_AsWiderAndNarrowerIntegralTypes()
    {
        var row = Row("bigint", 42L);

        Assert.Equal(42, row.GetFieldValue<int>(0));
        Assert.Equal((short)42, row.GetFieldValue<short>(0));
        Assert.Equal((byte)42, row.GetFieldValue<byte>(0));
        Assert.Equal((ushort)42, row.GetFieldValue<ushort>(0));
        Assert.Equal(42u, row.GetFieldValue<uint>(0));
        Assert.Equal(42ul, row.GetFieldValue<ulong>(0));
        Assert.Equal(42d, row.GetFieldValue<double>(0));
        Assert.Equal(42m, row.GetFieldValue<decimal>(0));
    }

    [Fact]
    public void IntegerColumn_AsNullableNarrowerType()
    {
        Assert.Equal(7, Row("integer", 7L).GetFieldValue<int?>(0));
        Assert.Equal(7L, Row("integer", 7L).GetFieldValue<long?>(0));
        Assert.Null(Row("integer", null).GetFieldValue<long?>(0));
    }

    [Fact]
    public void Narrowing_ThatLosesData_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => Row("bigint", 300L).GetFieldValue<byte>(0));
        Assert.Throws<OverflowException>(() => Row("bigint", -1L).GetFieldValue<uint>(0));
        Assert.Throws<OverflowException>(() => Row("bigint", (long)int.MaxValue + 1).GetFieldValue<int>(0));
    }

    [Fact]
    public void DecimalColumn_WithWholeValue_AsIntegralTypes()
    {
        Assert.Equal(12L, Row("decimal(10,0)", "12").GetFieldValue<long>(0));
        Assert.Equal(ulong.MaxValue, Row("decimal(20,0)", "18446744073709551615").GetFieldValue<ulong>(0));
    }

    [Fact]
    public void DecimalColumn_WithFraction_IsNotConvertedToIntegral()
    {
        Assert.Throws<InvalidCastException>(() => Row("decimal(10,2)", "12.50").GetFieldValue<long>(0));
    }

    [Fact]
    public void HighPrecisionDecimal_AsClrDecimal_WhenItFits()
    {
        Assert.Equal(1.25m, Row("decimal(38,2)", "1.25").GetFieldValue<decimal>(0));
        Assert.Throws<OverflowException>(() => Row("decimal(38,0)", new string('9', 38)).GetFieldValue<decimal>(0));
    }

    [Fact]
    public void HighPrecisionTimestamps_AsBclTypes()
    {
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1_234_567), Row("timestamp(9)", "2026-01-02 03:04:05.123456700").GetFieldValue<DateTime>(0));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_567), Row("timestamp(9) with time zone", "2026-01-02 03:04:05.123456700 UTC").GetFieldValue<DateTimeOffset>(0));
    }

    [Fact]
    public void DateAndTimestampWithTimeZone_AsDateTime()
    {
        Assert.Equal(new DateTime(2026, 1, 2), Row("date", "2026-01-02").GetFieldValue<DateTime>(0));

        var utc = Row("timestamp(3) with time zone", "2026-01-02 03:04:05.000 +02:00").GetFieldValue<DateTime>(0);
        Assert.Equal(new DateTime(2026, 1, 2, 1, 4, 5, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void VarcharColumn_AsGuidAndChar()
    {
        var guid = Guid.NewGuid();

        Assert.Equal(guid, Row("varchar", guid.ToString()).GetFieldValue<Guid>(0));
        Assert.Equal('x', Row("varchar", "x").GetFieldValue<char>(0));
        Assert.Throws<InvalidCastException>(() => Row("varchar", "not-a-guid").GetFieldValue<Guid>(0));
        Assert.Throws<InvalidCastException>(() => Row("varchar", "xy").GetFieldValue<char>(0));
    }

    [Fact]
    public void IntegerColumn_AsEnum()
    {
        Assert.Equal(Color.Green, Row("integer", 2L).GetFieldValue<Color>(0));
        Assert.Equal(Color.Red, Row("bigint", 1L).GetFieldValue<Color?>(0));
    }

    [Fact]
    public void UnsupportedConversion_ThrowsInvalidCastNamingTheColumn()
    {
        var ex = Assert.Throws<InvalidCastException>(() => Row("varchar", "abc").GetFieldValue<int>(0));

        Assert.Contains("'c'", ex.Message, StringComparison.Ordinal);
    }
}
