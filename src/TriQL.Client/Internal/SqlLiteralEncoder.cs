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
            // A typed NULL when the type is known: an untyped NULL is `unknown` to the analyzer, which
            // makes overloaded functions ambiguous (e.g. date_add(unit, n, NULL) fails with
            // AMBIGUOUS_FUNCTION_CALL) and lets others resolve to a surprising type (abs(NULL) is tinyint).
            return ResolveTypeName(parameter) is { } nullType ? $"CAST(NULL AS {nullType})" : "NULL";
        }

        if (parameter.TrinoType is { Length: > 0 } explicitType)
        {
            return EncodeWithExplicitType(parameter.Value, explicitType);
        }

        if (TryEncodeByClrType(parameter.Value, out var encoded))
        {
            return encoded;
        }

        if (parameter.DbType is not null && ResolveTypeName(parameter) is { } mappedType)
        {
            return EncodeWithExplicitType(parameter.Value, mappedType);
        }

        throw new TrinoParameterException(
            $"Cannot render a value of type '{parameter.Value.GetType()}' as a Trino literal. Set TrinoParameter.TrinoType or DbType explicitly.");
    }

    /// <summary>
    /// The Trino type a parameter declares: <see cref="TrinoParameter.TrinoType"/> if set, otherwise
    /// the mapping of <see cref="TrinoParameter.DbType"/>, with <see cref="TrinoParameter.Precision"/>
    /// and <see cref="TrinoParameter.Scale"/> applied to <c>decimal</c>.
    /// </summary>
    private static string? ResolveTypeName(TrinoParameter parameter)
    {
        if (parameter.TrinoType is { Length: > 0 } explicitType)
        {
            return explicitType;
        }

        if (parameter.DbType is not { } dbType || DbTypeMapping.ToTrinoTypeName(dbType) is not { } mapped)
        {
            return null;
        }

        return mapped == "decimal" && parameter.Precision is { } precision
            ? string.Create(CultureInfo.InvariantCulture, $"decimal({precision},{parameter.Scale ?? 0})")
            : mapped;
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
            sbyte or short or int or long or byte or ushort or uint => System.Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),

            // Beyond bigint's range a bare integer literal is invalid in Trino, so render those as decimal.
            ulong u => u <= long.MaxValue ? u.ToString(CultureInfo.InvariantCulture) : $"DECIMAL {EscapeString(u.ToString(CultureInfo.InvariantCulture))}",
            float f => EncodeFloatingPoint(f, f.ToString("R", CultureInfo.InvariantCulture), "REAL"),
            double d => EncodeFloatingPoint(d, d.ToString("R", CultureInfo.InvariantCulture), "DOUBLE"),
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

    /// <summary>
    /// A typed <c>REAL '…'</c>/<c>DOUBLE '…'</c> literal. A bare <c>1.5</c> would be <c>decimal(2,1)</c>
    /// in Trino, so the parameter would not have its CLR type (e.g. <c>SELECT ?</c> returned a decimal).
    /// <paramref name="roundTripText"/> is formatted from the original type, so a <see cref="float"/>
    /// does not carry the extra digits of its widened <see cref="double"/>.
    /// </summary>
    private static string EncodeFloatingPoint(double value, string roundTripText, string typeName)
    {
        var text = value switch
        {
            double.NaN => "NaN",
            double.PositiveInfinity => "Infinity",
            double.NegativeInfinity => "-Infinity",
            _ => roundTripText,
        };

        return $"{typeName} {EscapeString(text)}";
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

    /// <summary>Renders <paramref name="value"/> as a single-quoted SQL string literal, doubling embedded quotes.</summary>
    public static string EncodeStringLiteral(string value) => EscapeString(value);

    private static string EscapeString(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string EscapeHex(byte[] bytes) => "'" + System.Convert.ToHexString(bytes) + "'";
}
