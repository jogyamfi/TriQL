using System.Globalization;
using System.Net;
using TriQL.Client.Exceptions;
using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Converts raw decoded wire values (as produced by <see cref="RawJsonValueConverter"/>) into the
/// default CLR type for a column's <see cref="TrinoTypeSignature"/>. See FR-7.2.
/// </summary>
internal static class TrinoValueConverter
{
    /// <summary>Trino's precision for an unparameterized <c>decimal</c> is <c>decimal(38,0)</c>.</summary>
    private const int DefaultDecimalPrecision = 38;

    /// <summary>Trino's precision for an unparameterized <c>time</c>/<c>timestamp</c> is 3.</summary>
    private const int DefaultTemporalPrecision = 3;

    public static object? Convert(object? raw, TrinoTypeSignature type)
    {
        if (raw is null)
        {
            return null;
        }

        try
        {
            return type.BaseName switch
            {
                "boolean" => (bool)raw,
                "tinyint" => checked((sbyte)ToLong(raw)),
                "smallint" => checked((short)ToLong(raw)),
                "integer" => checked((int)ToLong(raw)),
                "bigint" => ToLong(raw),
                "real" => ToSingle(raw),
                "double" => ToDouble(raw),
                "decimal" => ConvertDecimal((string)raw, type),
                "varchar" or "char" or "json" => (string)raw,
                "varbinary" => System.Convert.FromBase64String((string)raw),
                "date" => DateOnly.ParseExact((string)raw, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                "time" => ConvertTime((string)raw, type),
                "timestamp" => ConvertTimestamp((string)raw, type),
                "interval" => ConvertInterval((string)raw, type),
                "uuid" => Guid.Parse((string)raw),
                "ipaddress" => IPAddress.Parse((string)raw),
                "array" => ComplexConverters.ConvertArray((object?[])raw, RequireSingleTypeArgument(type)),
                "map" => ComplexConverters.ConvertMap((Dictionary<string, object?>)raw, RequireKeyType(type), RequireValueType(type)),
                "row" => new TrinoRowValue(type, (object?[])raw),
                _ => raw,
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or IndexOutOfRangeException or ArgumentException)
        {
            throw new TrinoTypeConversionException($"The value '{raw}' could not be converted to Trino type '{type.RawSignature}'.", ex);
        }
    }

    /// <summary>The default CLR type for <paramref name="type"/>, per the FR-7.2.1 mapping table.</summary>
    public static Type GetDefaultClrType(TrinoTypeSignature type) => type.BaseName switch
    {
        "boolean" => typeof(bool),
        "tinyint" => typeof(sbyte),
        "smallint" => typeof(short),
        "integer" => typeof(int),
        "bigint" => typeof(long),
        "real" => typeof(float),
        "double" => typeof(double),
        "decimal" => UsesClrDecimal(type) ? typeof(decimal) : typeof(TrinoBigDecimal),
        "varchar" or "char" or "json" => typeof(string),
        "varbinary" => typeof(byte[]),
        "date" => typeof(DateOnly),
        "time" => type.WithTimeZone
            ? typeof(TrinoTimeWithTimeZone)
            : UsesClrTemporal(type) ? typeof(TimeOnly) : typeof(TrinoTime),
        "timestamp" => type.WithTimeZone
            ? UsesClrTemporal(type) ? typeof(DateTimeOffset) : typeof(TrinoTimestampWithTimeZone)
            : UsesClrTemporal(type) ? typeof(DateTime) : typeof(TrinoTimestamp),
        "interval" => type.IntervalRange == "year to month" ? typeof(TrinoIntervalYearToMonth) : typeof(TimeSpan),
        "uuid" => typeof(Guid),
        "ipaddress" => typeof(IPAddress),
        "array" => type.TypeArguments.Count == 1 ? GetArrayClrType(type.TypeArguments[0]) : typeof(object?[]),
        "map" => typeof(IReadOnlyDictionary<object, object?>),
        "row" => typeof(ITrinoRowValue),
        _ => typeof(object),
    };

    /// <summary>Whether a <c>decimal(p,s)</c> fits the BCL <see cref="decimal"/> without loss (FR-7.2.1).</summary>
    public static bool UsesClrDecimal(TrinoTypeSignature type) => (type.Precision ?? DefaultDecimalPrecision) <= 28;

    /// <summary>Whether a <c>time(p)</c>/<c>timestamp(p)</c> fits the BCL 100ns tick resolution (FR-7.2.1).</summary>
    public static bool UsesClrTemporal(TrinoTypeSignature type) => (type.Precision ?? DefaultTemporalPrecision) <= 7;

    /// <summary>
    /// The array CLR type for an <c>array(T)</c> element type. Enumerates known scalar leaf types
    /// explicitly rather than calling <c>Type.MakeArrayType()</c>, which is AOT/trim-incompatible.
    /// </summary>
    private static Type GetArrayClrType(TrinoTypeSignature elementType) => elementType.BaseName switch
    {
        "boolean" => typeof(bool[]),
        "tinyint" => typeof(sbyte[]),
        "smallint" => typeof(short[]),
        "integer" => typeof(int[]),
        "bigint" => typeof(long[]),
        "real" => typeof(float[]),
        "double" => typeof(double[]),
        "decimal" => UsesClrDecimal(elementType) ? typeof(decimal[]) : typeof(TrinoBigDecimal[]),
        "varchar" or "char" or "json" => typeof(string[]),
        "varbinary" => typeof(byte[][]),
        "date" => typeof(DateOnly[]),
        "time" => elementType.WithTimeZone
            ? typeof(TrinoTimeWithTimeZone[])
            : UsesClrTemporal(elementType) ? typeof(TimeOnly[]) : typeof(TrinoTime[]),
        "timestamp" => elementType.WithTimeZone
            ? UsesClrTemporal(elementType) ? typeof(DateTimeOffset[]) : typeof(TrinoTimestampWithTimeZone[])
            : UsesClrTemporal(elementType) ? typeof(DateTime[]) : typeof(TrinoTimestamp[]),
        "interval" => elementType.IntervalRange == "year to month" ? typeof(TrinoIntervalYearToMonth[]) : typeof(TimeSpan[]),
        "uuid" => typeof(Guid[]),
        "ipaddress" => typeof(IPAddress[]),
        _ => typeof(object?[]),
    };

    private static TrinoTypeSignature RequireSingleTypeArgument(TrinoTypeSignature type) =>
        type.TypeArguments.Count == 1
            ? type.TypeArguments[0]
            : throw new TrinoTypeConversionException($"'{type.RawSignature}' does not declare an array element type.");

    private static TrinoTypeSignature RequireKeyType(TrinoTypeSignature type) =>
        type.TypeArguments.Count == 2
            ? type.TypeArguments[0]
            : throw new TrinoTypeConversionException($"'{type.RawSignature}' does not declare map key/value types.");

    private static TrinoTypeSignature RequireValueType(TrinoTypeSignature type) => type.TypeArguments[1];

    private static long ToLong(object raw) => raw switch
    {
        long l => l,
        int i => i,
        double d => checked((long)d),
        _ => System.Convert.ToInt64(raw, CultureInfo.InvariantCulture),
    };

    private static float ToSingle(object raw) => raw switch
    {
        double d => (float)d,
        long l => l,
        string s => ParseSpecialSingle(s),
        _ => System.Convert.ToSingle(raw, CultureInfo.InvariantCulture),
    };

    private static double ToDouble(object raw) => raw switch
    {
        double d => d,
        long l => l,
        string s => ParseSpecialDouble(s),
        _ => System.Convert.ToDouble(raw, CultureInfo.InvariantCulture),
    };

    private static double ParseSpecialDouble(string s) => s switch
    {
        "NaN" => double.NaN,
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => double.Parse(s, CultureInfo.InvariantCulture),
    };

    private static float ParseSpecialSingle(string s) => s switch
    {
        "NaN" => float.NaN,
        "Infinity" => float.PositiveInfinity,
        "-Infinity" => float.NegativeInfinity,
        _ => float.Parse(s, CultureInfo.InvariantCulture),
    };

    private static object ConvertDecimal(string text, TrinoTypeSignature type) => UsesClrDecimal(type)
        ? decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
        : TrinoBigDecimal.Parse(text);

    private static object ConvertTime(string text, TrinoTypeSignature type)
    {
        if (type.WithTimeZone)
        {
            return TrinoTimeWithTimeZone.Parse(text);
        }

        var time = TrinoTime.Parse(text);
        return UsesClrTemporal(type) ? (TimeOnly)time : time;
    }

    private static object ConvertTimestamp(string text, TrinoTypeSignature type)
    {
        if (type.WithTimeZone)
        {
            var zoned = TrinoTimestampWithTimeZone.Parse(text);
            return UsesClrTemporal(type) ? (DateTimeOffset)zoned : zoned;
        }

        var timestamp = TrinoTimestamp.Parse(text);
        return UsesClrTemporal(type) ? (DateTime)timestamp : timestamp;
    }

    private static object ConvertInterval(string text, TrinoTypeSignature type) => type.IntervalRange switch
    {
        "year to month" => TrinoIntervalYearToMonth.Parse(text),
        "day to second" => ParseIntervalDayToSecond(text),
        _ => throw new TrinoTypeConversionException($"Unknown interval range '{type.IntervalRange}'."),
    };

    private static TimeSpan ParseIntervalDayToSecond(string text)
    {
        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var spaceIndex = body.IndexOf(' ');
        if (spaceIndex < 0)
        {
            throw new FormatException($"'{text}' is not a valid day-to-second interval.");
        }

        var days = int.Parse(body[..spaceIndex], CultureInfo.InvariantCulture);
        var time = TimeSpan.ParseExact(body[(spaceIndex + 1)..], @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        var total = TimeSpan.FromDays(days) + time;
        return negative ? -total : total;
    }
}
