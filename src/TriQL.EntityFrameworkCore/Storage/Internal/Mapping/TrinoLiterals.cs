using System.Globalization;

namespace TriQL.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>
/// Trino typed-literal rendering shared by the type mappings: <c>TYPE 'text'</c> forms, whose type
/// (including timestamp/time precision, which Trino takes from the number of fractional digits) is
/// exact rather than inferred from the shape of a bare literal.
/// </summary>
internal static class TrinoLiterals
{
    /// <summary>.NET's finest temporal precision: 7 fractional digits (100 ns ticks).</summary>
    public const int MaxClrFractionalDigits = 7;

    /// <summary>Renders <paramref name="text"/> as <c>TYPE 'text'</c>, escaping quotes.</summary>
    public static string Typed(string typeName, string text) => typeName + " " + TrinoSqlGenerationHelper.GenerateStringLiteral(text);

    /// <summary>A <c>REAL</c>/<c>DOUBLE</c> literal, including the NaN and infinity specials, with round-trip digits.</summary>
    public static string FloatingPoint(string typeName, double value, string roundTripText) => value switch
    {
        double.NaN => Typed(typeName, "NaN"),
        double.PositiveInfinity => Typed(typeName, "Infinity"),
        double.NegativeInfinity => Typed(typeName, "-Infinity"),
        _ => Typed(typeName, roundTripText),
    };

    /// <summary>A <c>TIMESTAMP</c> literal with <paramref name="precision"/> fractional digits (at most 7), truncating finer ticks.</summary>
    public static string Timestamp(DateTime value, int precision) =>
        Typed("TIMESTAMP", value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(value.Ticks, precision));

    /// <summary>
    /// A <c>TIMESTAMP '… +hh:mm'</c> literal keeping <paramref name="value"/>'s own offset, as parameters
    /// do, so a value reads back the same whether EF inlined it or bound it. Connectors that store an
    /// instant (Iceberg) return it in UTC.
    /// </summary>
    public static string TimestampWithTimeZone(DateTimeOffset value, int precision) =>
        Typed(
            "TIMESTAMP",
            value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(value.Ticks, precision)
            + " " + value.ToString("zzz", CultureInfo.InvariantCulture));

    /// <summary>A <c>TIME</c> literal with <paramref name="precision"/> fractional digits (at most 7).</summary>
    public static string Time(TimeOnly value, int precision) =>
        Typed("TIME", value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(value.Ticks, precision));

    private static string Fraction(long ticks, int precision)
    {
        var digits = Math.Clamp(precision, 0, MaxClrFractionalDigits);
        if (digits == 0)
        {
            return string.Empty;
        }

        var subSecondTicks = ticks % TimeSpan.TicksPerSecond;
        return "." + subSecondTicks.ToString("D7", CultureInfo.InvariantCulture)[..digits];
    }
}
