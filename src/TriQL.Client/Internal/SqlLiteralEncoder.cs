using System.Globalization;
using System.Net;
using TriQL.Client.Exceptions;
using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Renders <see cref="TrinoParameter"/> values as Trino SQL literals for the <c>EXECUTE ... USING</c>
/// clause. Never concatenates unescaped caller input; rejects values it cannot render rather than
/// falling back to <c>ToString()</c>. See FR-8.3, SEC-4.
/// </summary>
internal static class SqlLiteralEncoder
{
    public static string Encode(TrinoParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter.Value is null)
        {
            return "NULL";
        }

        if (parameter.TrinoType is { Length: > 0 } explicitType)
        {
            return EncodeWithExplicitType(parameter.Value, explicitType);
        }

        if (TryEncodeByClrType(parameter.Value, out var encoded))
        {
            return encoded;
        }

        if (parameter.DbType is { } dbType && DbTypeMapping.ToTrinoTypeName(dbType) is { } mappedType)
        {
            return EncodeWithExplicitType(parameter.Value, mappedType);
        }

        throw new TrinoParameterException(
            $"Cannot render a value of type '{parameter.Value.GetType()}' as a Trino literal. Set TrinoParameter.TrinoType or DbType explicitly.");
    }

    private static string EncodeWithExplicitType(object value, string trinoType)
    {
        var text = value switch
        {
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new TrinoParameterException($"Cannot render a value of type '{value.GetType()}' via explicit Trino type '{trinoType}'."),
        };

        return $"CAST({EscapeString(text)} AS {trinoType})";
    }

    private static bool TryEncodeByClrType(object value, out string encoded)
    {
        encoded = value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            sbyte or short or int or long => System.Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            float f => EncodeFloatingPoint(f, double.IsNaN(f), double.IsPositiveInfinity(f), double.IsNegativeInfinity(f), "REAL"),
            double d => EncodeFloatingPoint(d, double.IsNaN(d), double.IsPositiveInfinity(d), double.IsNegativeInfinity(d), "DOUBLE"),
            decimal dec => $"DECIMAL {EscapeString(dec.ToString(CultureInfo.InvariantCulture))}",
            TrinoBigDecimal bd => $"DECIMAL {EscapeString(bd.ToString(null, CultureInfo.InvariantCulture))}",
            string s => EscapeString(s),
            byte[] bytes => $"X{EscapeHex(bytes)}",
            Guid g => $"UUID {EscapeString(g.ToString())}",
            DateOnly date => $"DATE {EscapeString(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            TimeOnly time => $"TIME {EscapeString(time.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture))}",
            DateTime dt => $"TIMESTAMP {EscapeString(dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture))}",
            DateTimeOffset dto => $"TIMESTAMP {EscapeString(dto.ToString("yyyy-MM-dd HH:mm:ss.ffffff zzz", CultureInfo.InvariantCulture))}",
            TrinoTimestamp ts => $"TIMESTAMP {EscapeString(ts.ToString(null, CultureInfo.InvariantCulture))}",
            TrinoTime t => $"TIME {EscapeString(t.ToString(null, CultureInfo.InvariantCulture))}",
            TrinoTimeWithTimeZone twz => $"TIME {EscapeString(twz.ToString(null, CultureInfo.InvariantCulture))}",
            TrinoTimestampWithTimeZone tswz => $"TIMESTAMP {EscapeString(tswz.ToString(null, CultureInfo.InvariantCulture))}",
            TrinoIntervalYearToMonth iym => $"INTERVAL {EscapeString(iym.ToString(null, CultureInfo.InvariantCulture))} YEAR TO MONTH",
            TimeSpan interval => $"INTERVAL {EscapeString(FormatIntervalDayToSecond(interval))} DAY TO SECOND",
            IPAddress ip => $"CAST({EscapeString(ip.ToString())} AS IPADDRESS)",
            _ => null,
        } ?? string.Empty;

        return encoded.Length > 0;
    }

    private static string EncodeFloatingPoint(double value, bool isNaN, bool isPositiveInfinity, bool isNegativeInfinity, string typeName)
    {
        if (isNaN)
        {
            return $"CAST('NaN' AS {typeName})";
        }

        if (isPositiveInfinity)
        {
            return $"CAST('Infinity' AS {typeName})";
        }

        if (isNegativeInfinity)
        {
            return $"CAST('-Infinity' AS {typeName})";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatIntervalDayToSecond(TimeSpan value)
    {
        var negative = value < TimeSpan.Zero;
        var magnitude = value.Duration();
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{magnitude.Days} {magnitude.Hours:D2}:{magnitude.Minutes:D2}:{magnitude.Seconds:D2}.{magnitude.Milliseconds:D3}");
        return negative ? "-" + text : text;
    }

    private static string EscapeString(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string EscapeHex(byte[] bytes) => "'" + System.Convert.ToHexString(bytes) + "'";
}
