using System.Net;
using System.Numerics;
using System.Text.Json;
using TriQL.Client;
using TriQL.Client.Types;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P3-T17: round-trips every FR-7.2.1 row through <c>SELECT</c> against a real container, plus the
/// Phase 3 exit criteria that call out specific precision/zone behavior: <c>tinyint</c> → <c>sbyte</c>
/// with negative-value survival, <c>timestamp(12)</c> picosecond precision, <c>decimal(38,10)</c> full
/// precision, and session-time-zone governance of <c>with time zone</c> conversion.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class TypeRoundTripTests(TrinoContainerFixture fixture)
{
    private static async Task<TrinoRow> ExecuteSingleRowAsync(TrinoContainerFixture fixture, string sql, TrinoSessionOptions? options = null)
    {
        options ??= new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);
        await using var resultSet = await client.ExecuteAsync(sql);

        var rows = new List<TrinoRow>();
        await foreach (var row in resultSet.ReadRowsAsync())
        {
            rows.Add(row.Clone());
        }

        Assert.Single(rows);
        return rows[0];
    }

    // --- boolean --------------------------------------------------------

    [Fact]
    public async Task Boolean_RoundTrips_AsBool()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT true");

        Assert.Equal(typeof(bool), row.GetFieldType(0));
        Assert.True((bool)row.GetValue(0)!);
    }

    // --- tinyint (sbyte, signed — see FR-7.2.1 note) --------------------

    [Fact]
    public async Task Tinyint_RoundTrips_AsSByte()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(100 AS TINYINT)");

        Assert.Equal(typeof(sbyte), row.GetFieldType(0));
        Assert.Equal((sbyte)100, row.GetValue(0));
    }

    [Fact]
    public async Task Tinyint_NegativeValue_Survives_AsSByte()
    {
        // Trino tinyint is SIGNED; mapping to byte (unsigned) would be a defect (FR-7.2.1 note).
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(-128 AS TINYINT)");

        Assert.Equal(typeof(sbyte), row.GetFieldType(0));
        Assert.Equal((sbyte)-128, row.GetValue(0));
        Assert.IsType<sbyte>(row.GetValue(0));
    }

    // --- smallint / integer / bigint ------------------------------------

    [Fact]
    public async Task Smallint_RoundTrips_AsShort()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(-32768 AS SMALLINT)");

        Assert.Equal(typeof(short), row.GetFieldType(0));
        Assert.Equal((short)-32768, row.GetValue(0));
    }

    [Fact]
    public async Task Integer_RoundTrips_AsInt()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(-2147483648 AS INTEGER)");

        Assert.Equal(typeof(int), row.GetFieldType(0));
        Assert.Equal(-2147483648, row.GetValue(0));
    }

    [Fact]
    public async Task Bigint_RoundTrips_AsLong()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(-9223372036854775808 AS BIGINT)");

        Assert.Equal(typeof(long), row.GetFieldType(0));
        Assert.Equal(-9223372036854775808L, row.GetValue(0));
    }

    // --- real / double ----------------------------------------------------

    [Fact]
    public async Task Real_RoundTrips_AsFloat()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(3.14 AS REAL)");

        Assert.Equal(typeof(float), row.GetFieldType(0));
        Assert.Equal(3.14f, (float)row.GetValue(0)!, 5);
    }

    [Fact]
    public async Task Double_RoundTrips_AsDouble()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(3.14159265358979 AS DOUBLE)");

        Assert.Equal(typeof(double), row.GetFieldType(0));
        Assert.Equal(3.14159265358979, (double)row.GetValue(0)!, 10);
    }

    // --- decimal ------------------------------------------------------------

    [Fact]
    public async Task Decimal_PrecisionAtOrBelow28_RoundTrips_AsClrDecimal()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(1234.5678 AS DECIMAL(20,4))");

        Assert.Equal(typeof(decimal), row.GetFieldType(0));
        Assert.Equal(1234.5678m, row.GetValue(0));
    }

    [Fact]
    public async Task Decimal_PrecisionAbove28_RoundTrips_AsTrinoBigDecimal()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(1.5 AS DECIMAL(38,10))");

        Assert.Equal(typeof(TrinoBigDecimal), row.GetFieldType(0));
        Assert.Equal(new TrinoBigDecimal(new BigInteger(15_000_000_000L), 10), row.GetValue(0));
    }

    [Fact]
    public async Task Decimal38_10_RetainsFullPrecision_ThroughTrinoBigDecimal()
    {
        // decimal(38,10) is the maximum precision Trino supports; 28 integer digits + 10 fractional
        // digits = 38 significant digits, exercising the full width DECIMAL cannot losslessly hold.
        var integerPart = new string('7', 28);
        var fractionalPart = new string('3', 10);
        var sql = $"SELECT CAST('{integerPart}.{fractionalPart}' AS DECIMAL(38,10))";

        var row = await ExecuteSingleRowAsync(fixture, sql);

        Assert.Equal(typeof(TrinoBigDecimal), row.GetFieldType(0));
        var value = Assert.IsType<TrinoBigDecimal>(row.GetValue(0));

        var expectedUnscaled = BigInteger.Parse(integerPart + fractionalPart, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(new TrinoBigDecimal(expectedUnscaled, 10), value);
        Assert.Equal($"{integerPart}.{fractionalPart}", value.ToString());
    }

    // --- varchar / char / varbinary / json -----------------------------------

    [Fact]
    public async Task Varchar_RoundTrips_AsString()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT 'hello, trino'");

        Assert.Equal(typeof(string), row.GetFieldType(0));
        Assert.Equal("hello, trino", row.GetValue(0));
    }

    [Fact]
    public async Task VarcharN_RoundTrips_AsString()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST('hi' AS VARCHAR(10))");

        Assert.Equal(typeof(string), row.GetFieldType(0));
        Assert.Equal("hi", row.GetValue(0));
    }

    [Fact]
    public async Task CharN_RoundTrips_AsString_WithPaddingPreserved()
    {
        // char(n) is space-padded by the server; the padding MUST be preserved (FR-7.2.1 note).
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST('ab' AS CHAR(5))");

        Assert.Equal(typeof(string), row.GetFieldType(0));
        Assert.Equal("ab   ", row.GetValue(0));
    }

    [Fact]
    public async Task Varbinary_RoundTrips_AsByteArray()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT X'DEADBEEF'");

        Assert.Equal(typeof(byte[]), row.GetFieldType(0));
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, row.GetValue(0));
    }

    [Fact]
    public async Task Json_RoundTrips_AsString()
    {
        // CAST(varchar AS JSON) wraps the source text as a JSON *string* scalar rather than parsing
        // it — the JSON 'literal' form (or json_parse()) is what produces a JSON object value here.
        var row = await ExecuteSingleRowAsync(fixture, "SELECT JSON '{\"a\":1}'");

        Assert.Equal(typeof(string), row.GetFieldType(0));
        Assert.Equal("{\"a\":1}", row.GetValue(0));
    }

    [Fact]
    public async Task Json_IsAlsoAccessible_AsJsonDocument()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT JSON '{\"a\":1}'");

        using var document = row.GetFieldValue<JsonDocument>(0);
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
    }

    // --- date / time / timestamp (untyped) -------------------------------

    [Fact]
    public async Task Date_RoundTrips_AsDateOnly()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT DATE '2024-06-15'");

        Assert.Equal(typeof(DateOnly), row.GetFieldType(0));
        Assert.Equal(new DateOnly(2024, 6, 15), row.GetValue(0));
    }

    [Fact]
    public async Task TimeAtOrBelowPrecision7_RoundTrips_AsTimeOnly()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(TIME '12:34:56.123456' AS TIME(6))");

        Assert.Equal(typeof(TimeOnly), row.GetFieldType(0));
        Assert.Equal(TimeOnly.Parse("12:34:56.1234560", System.Globalization.CultureInfo.InvariantCulture), row.GetValue(0));
    }

    [Fact]
    public async Task TimeAbovePrecision7_RoundTrips_AsTrinoTime()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(TIME '23:59:59.123456789' AS TIME(9))");

        Assert.Equal(typeof(TrinoTime), row.GetFieldType(0));
        Assert.Equal(TrinoTime.Parse("23:59:59.123456789000"), row.GetValue(0));
    }

    [Fact]
    public async Task TimeWithTimeZone_RoundTrips_AsTrinoTimeWithTimeZone()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT TIME '12:00:00+05:30'");

        Assert.Equal(typeof(TrinoTimeWithTimeZone), row.GetFieldType(0));
        var value = Assert.IsType<TrinoTimeWithTimeZone>(row.GetValue(0));
        Assert.Equal(TimeSpan.FromMinutes(5 * 60 + 30), value.Offset);
        Assert.Equal(TrinoTimeWithTimeZone.Parse("12:00:00+05:30"), value);
    }

    [Fact]
    public async Task TimestampAtOrBelowPrecision7_RoundTrips_AsDateTime()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(TIMESTAMP '2024-06-15 10:20:30.123456' AS TIMESTAMP(6))");

        Assert.Equal(typeof(DateTime), row.GetFieldType(0));
        var value = Assert.IsType<DateTime>(row.GetValue(0));
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
        Assert.Equal(DateTime.Parse("2024-06-15 10:20:30.1234560", System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public async Task TimestampAbovePrecision7_RoundTrips_AsTrinoTimestamp_WithPicosecondPrecision()
    {
        // Phase 3 exit criterion: timestamp(12) retains picosecond precision through TrinoTimestamp.
        const string text = "2024-03-14 09:26:53.123456789012";
        var row = await ExecuteSingleRowAsync(fixture, $"SELECT CAST(TIMESTAMP '{text}' AS TIMESTAMP(12))");

        Assert.Equal(typeof(TrinoTimestamp), row.GetFieldType(0));
        var value = Assert.IsType<TrinoTimestamp>(row.GetValue(0));
        var expected = TrinoTimestamp.Parse(text);

        Assert.Equal(expected, value);
        Assert.Equal(new DateOnly(2024, 3, 14), value.Date);
        // Sub-100ns precision (the trailing "012" picoseconds) must survive: converting to DateTime
        // (100ns ticks) must throw rather than silently truncate.
        Assert.Throws<OverflowException>(() => (DateTime)value);
    }

    [Fact]
    public async Task TimestampWithTimeZoneAtOrBelowPrecision7_RoundTrips_AsDateTimeOffset()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT TIMESTAMP '2024-06-15 10:20:30.123 +02:00'");

        Assert.Equal(typeof(DateTimeOffset), row.GetFieldType(0));
        var value = Assert.IsType<DateTimeOffset>(row.GetValue(0));
        Assert.Equal(DateTimeOffset.Parse("2024-06-15 10:20:30.123 +02:00", System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public async Task TimestampWithTimeZoneAbovePrecision7_RoundTrips_AsTrinoTimestampWithTimeZone()
    {
        const string text = "2024-06-15 10:20:30.123456789012 +02:00";
        var row = await ExecuteSingleRowAsync(fixture, $"SELECT CAST(TIMESTAMP '{text}' AS TIMESTAMP(12) WITH TIME ZONE)");

        Assert.Equal(typeof(TrinoTimestampWithTimeZone), row.GetFieldType(0));
        var value = Assert.IsType<TrinoTimestampWithTimeZone>(row.GetValue(0));
        Assert.Equal(TrinoTimestampWithTimeZone.Parse(text), value);
        Assert.Equal(TimeSpan.FromHours(2), value.Offset);
    }

    // --- intervals --------------------------------------------------------

    [Fact]
    public async Task IntervalYearToMonth_RoundTrips_AsTrinoIntervalYearToMonth()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT INTERVAL '1-2' YEAR TO MONTH");

        Assert.Equal(typeof(TrinoIntervalYearToMonth), row.GetFieldType(0));
        var value = Assert.IsType<TrinoIntervalYearToMonth>(row.GetValue(0));
        Assert.Equal(14, value.TotalMonths);
        Assert.Equal(1, value.Years);
        Assert.Equal(2, value.Months);
    }

    [Fact]
    public async Task IntervalDayToSecond_RoundTrips_AsTimeSpan()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT INTERVAL '3 04:05:06.789' DAY TO SECOND");

        Assert.Equal(typeof(TimeSpan), row.GetFieldType(0));
        Assert.Equal(new TimeSpan(3, 4, 5, 6, 789), row.GetValue(0));
    }

    // --- uuid / ipaddress ---------------------------------------------------

    [Fact]
    public async Task Uuid_RoundTrips_AsGuid()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT UUID 'd3b07384-d113-4b8f-8a0e-2f1c47f9d1e2'");

        Assert.Equal(typeof(Guid), row.GetFieldType(0));
        Assert.Equal(Guid.Parse("d3b07384-d113-4b8f-8a0e-2f1c47f9d1e2"), row.GetValue(0));
    }

    [Fact]
    public async Task IpAddress_V4_RoundTrips_AsIPAddress()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST('192.168.1.1' AS IPADDRESS)");

        Assert.Equal(typeof(IPAddress), row.GetFieldType(0));
        Assert.Equal(IPAddress.Parse("192.168.1.1"), row.GetValue(0));
    }

    [Fact]
    public async Task IpAddress_V6_RoundTrips_AsIPAddress()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST('2001:db8::1' AS IPADDRESS)");

        Assert.Equal(typeof(IPAddress), row.GetFieldType(0));
        Assert.Equal(IPAddress.Parse("2001:db8::1"), row.GetValue(0));
    }

    // --- array / map / row -----------------------------------------------

    [Fact]
    public async Task ArrayOfInteger_RoundTrips_AsIntArray()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT ARRAY[1, 2, 3]");

        Assert.Equal(typeof(int[]), row.GetFieldType(0));
        int[] expected = [1, 2, 3];
        Assert.Equal(expected, row.GetValue(0));
    }

    [Fact]
    public async Task ArrayOfInteger_WithNullElement_FallsBackToObjectArray()
    {
        // A null element is only representable in T[] when T is a reference type, so a value-typed
        // element array containing NULL falls back to object?[] (per ComplexConverters/FR-7.2.1).
        var row = await ExecuteSingleRowAsync(fixture, "SELECT ARRAY[1, NULL, 3]");

        Assert.Equal(typeof(object?[]), row.GetValue(0)!.GetType());
        Assert.Equal(new object?[] { 1, null, 3 }, row.GetValue(0));
    }

    [Fact]
    public async Task MapOfVarcharToInteger_RoundTrips_AsReadOnlyDictionary()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT MAP(ARRAY['a', 'b'], ARRAY[1, 2])");

        var value = Assert.IsAssignableFrom<IReadOnlyDictionary<object, object?>>(row.GetValue(0));
        Assert.Equal(2, value.Count);
        Assert.Equal(1, value["a"]);
        Assert.Equal(2, value["b"]);
    }

    [Fact]
    public async Task Row_RoundTrips_AsITrinoRowValue()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(ROW(1, 'a') AS ROW(x INTEGER, y VARCHAR))");

        Assert.Equal(typeof(ITrinoRowValue), row.GetFieldType(0));
        var value = Assert.IsAssignableFrom<ITrinoRowValue>(row.GetValue(0));
        Assert.Equal(2, value.FieldCount);
        Assert.Equal(1, value[0]);
        Assert.Equal("a", value[1]);
        Assert.Equal(1, value["x"]);
        Assert.Equal("a", value["y"]);
        Assert.Equal(0, value.GetOrdinal("x"));
        Assert.Equal("y", value.GetName(1));
    }

    // --- null / unknown ----------------------------------------------------

    [Fact]
    public async Task Null_RoundTrips_AsNullValue()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT NULL");

        Assert.Null(row.GetValue(0));
        Assert.True(row.IsDBNull(0));
    }

    [Fact]
    public async Task TypedNull_RoundTrips_AsNullValue_WithoutLosingTheColumnType()
    {
        var row = await ExecuteSingleRowAsync(fixture, "SELECT CAST(NULL AS INTEGER)");

        Assert.Equal(typeof(int), row.GetFieldType(0));
        Assert.Null(row.GetValue(0));
        Assert.True(row.IsDBNull(0));
    }

    // --- session time zone governance --------------------------------------

    [Fact]
    public async Task WithTimeZoneConversion_IsGovernedBySessionTimeZone_NotHostLocalZone()
    {
        // Pacific/Kiritimati is UTC+14, has no DST, and is (barring an extraordinarily unlucky CI
        // host) never the machine's local zone — a strong, deterministic proof that the client uses
        // the configured session zone rather than TimeZoneInfo.Local (FR-7.2.5).
        const string sessionZone = "Pacific/Kiritimati";
        Assert.NotEqual(sessionZone, TimeZoneInfo.Local.Id);

        var options = new TrinoSessionOptions { Server = fixture.ServerUri, TimeZone = sessionZone };

        // CAST of a plain (zoneless) TIMESTAMP to TIMESTAMP WITH TIME ZONE interprets the wall-clock
        // value in the *session* time zone, per Trino semantics — exactly the behavior FR-7.2.5 requires.
        var row = await ExecuteSingleRowAsync(
            fixture, "SELECT CAST(TIMESTAMP '2024-06-15 08:00:00' AS TIMESTAMP(3) WITH TIME ZONE)", options);

        Assert.Equal(typeof(DateTimeOffset), row.GetFieldType(0));
        var value = Assert.IsType<DateTimeOffset>(row.GetValue(0));

        Assert.Equal(TimeSpan.FromHours(14), value.Offset);
        Assert.Equal(new DateTime(2024, 6, 15, 8, 0, 0), value.DateTime);
    }
}
