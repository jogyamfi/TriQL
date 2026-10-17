using System.Numerics;
using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// The conversions <see cref="TrinoRow.GetFieldValue{T}"/> applies when the materialized value is
/// not already of the requested type — what ORMs such as EF Core rely on when a column's CLR type
/// differs from the model's (e.g. <c>COUNT(*)</c> is <c>bigint</c> but the model asks for
/// <see cref="int"/>). Every narrowing is checked: a value that does not fit throws
/// <see cref="OverflowException"/> rather than being truncated (FR-7.2.4).
/// </summary>
internal static class FieldValueConverter
{
    /// <summary>
    /// Converts <paramref name="value"/> (a non-null default-CLR-type value) to
    /// <paramref name="target"/>, a non-nullable type. Returns <see langword="false"/> when no
    /// conversion exists; throws <see cref="OverflowException"/> when one exists but loses data.
    /// </summary>
    public static bool TryConvert(object value, Type target, out object? result)
    {
        result = target switch
        {
            _ when target == typeof(sbyte) => IsIntegral(value) ? NumericAccessors.ToSByte(Integral(value)) : null,
            _ when target == typeof(short) => IsIntegral(value) ? NumericAccessors.ToInt16(Integral(value)) : null,
            _ when target == typeof(int) => IsIntegral(value) ? NumericAccessors.ToInt32(Integral(value)) : null,
            _ when target == typeof(long) => IsIntegral(value) ? Integral(value) : null,
            _ when target == typeof(byte) => IsIntegral(value) ? checked((byte)Integral(value)) : null,
            _ when target == typeof(ushort) => IsIntegral(value) ? checked((ushort)Integral(value)) : null,
            _ when target == typeof(uint) => IsIntegral(value) ? checked((uint)Integral(value)) : null,
            _ when target == typeof(ulong) => ToUInt64(value),
            _ when target == typeof(float) => IsNumeric(value) ? NumericAccessors.ToSingle(value) : null,
            _ when target == typeof(double) => IsNumeric(value) ? NumericAccessors.ToDouble(value) : null,
            _ when target == typeof(decimal) => ToDecimal(value),
            _ when target == typeof(DateTime) => ToDateTime(value),
            _ when target == typeof(DateTimeOffset) => value is TrinoTimestampWithTimeZone tz ? (DateTimeOffset)tz : null,
            _ when target == typeof(TimeOnly) => value is TrinoTime t ? (TimeOnly)t : null,
            _ when target == typeof(Guid) => value is string s && Guid.TryParse(s, out var g) ? g : null,
            _ when target == typeof(char) => value is string { Length: 1 } c ? c[0] : null,
            _ when target.IsEnum && IsIntegral(value) => Enum.ToObject(target, Integral(value)),
            _ => null,
        };

        return result is not null;
    }

    private static bool IsIntegral(object value) =>
        value is sbyte or short or int or long || (value is decimal or TrinoBigDecimal && IsWhole(value));

    private static bool IsNumeric(object value) => value is sbyte or short or int or long or float or double;

    private static bool IsWhole(object value) => value switch
    {
        decimal d => d == decimal.Truncate(d),
        TrinoBigDecimal bd => bd.Scale <= 0 || BigInteger.Remainder(bd.UnscaledValue, BigInteger.Pow(10, bd.Scale)).IsZero,
        _ => false,
    };

    /// <summary>An integral (or whole-valued decimal) source as <see cref="long"/>, checked.</summary>
    private static long Integral(object value) => value switch
    {
        decimal d => checked((long)d),
        TrinoBigDecimal bd => checked((long)(decimal)bd),
        _ => NumericAccessors.ToInt64(value),
    };

    private static object? ToUInt64(object value) => value switch
    {
        // ulong exceeds long, so a decimal(20,0) source converts directly rather than via Integral.
        decimal d when d == decimal.Truncate(d) => checked((ulong)d),
        TrinoBigDecimal bd when IsWhole(bd) => checked((ulong)(decimal)bd),
        sbyte or short or int or long => checked((ulong)NumericAccessors.ToInt64(value)),
        _ => null,
    };

    private static object? ToDecimal(object value) => value switch
    {
        sbyte or short or int or long or decimal or TrinoBigDecimal => NumericAccessors.ToDecimal(value),
        float f => (decimal)f,
        double d => (decimal)d,
        _ => null,
    };

    private static object? ToDateTime(object value) => value switch
    {
        TrinoTimestamp ts => (DateTime)ts,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset dto => dto.UtcDateTime,
        _ => null,
    };
}
