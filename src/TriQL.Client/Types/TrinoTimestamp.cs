using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>
/// A Trino <c>timestamp(p)</c> value where <c>p &gt; 7</c>, exceeding the 100 ns resolution of
/// <see cref="DateTime"/>. Stores date plus time-of-day at picosecond resolution. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoTimestamp :
    IEquatable<TrinoTimestamp>,
    IComparable<TrinoTimestamp>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoTimestamp>
{
    private const long PicosecondsPerTick = 100_000L;

    /// <summary>Initializes a new instance from a date and a picosecond-of-day offset.</summary>
    public TrinoTimestamp(DateOnly date, long picosecondOfDay)
    {
        Date = date;
        PicosecondOfDay = new TrinoTime(picosecondOfDay).PicosecondOfDay;
    }

    /// <summary>The calendar date component.</summary>
    public DateOnly Date { get; }

    /// <summary>The time-of-day component, in picoseconds since midnight.</summary>
    public long PicosecondOfDay { get; }

    /// <summary>Explicit narrowing conversion to <see cref="DateTime"/> (<c>Kind = Unspecified</c>). Throws on sub-100ns precision loss.</summary>
    public static explicit operator DateTime(TrinoTimestamp value)
    {
        var ticks = Math.DivRem(value.PicosecondOfDay, PicosecondsPerTick, out var remainder);
        if (remainder != 0)
        {
            throw new OverflowException($"'{value}' has sub-100ns precision that cannot be represented by DateTime.");
        }

        return value.Date.ToDateTime(new TimeOnly(ticks), DateTimeKind.Unspecified);
    }

    /// <inheritdoc/>
    public bool Equals(TrinoTimestamp other) => Date == other.Date && PicosecondOfDay == other.PicosecondOfDay;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoTimestamp other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Date, PicosecondOfDay);

    /// <inheritdoc/>
    public int CompareTo(TrinoTimestamp other)
    {
        var dateCompare = Date.CompareTo(other.Date);
        return dateCompare != 0 ? dateCompare : PicosecondOfDay.CompareTo(other.PicosecondOfDay);
    }

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => TrinoTemporalText.FormatTimestamp(Date, PicosecondOfDay);

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = ToString(format.ToString(), provider);
        if (text.Length > destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        text.CopyTo(destination);
        charsWritten = text.Length;
        return true;
    }

    /// <inheritdoc/>
    public static TrinoTimestamp Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a value formatted as <c>"yyyy-MM-dd HH:mm:ss[.ffffffffffff]"</c>.</summary>
    public static TrinoTimestamp Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid timestamp value.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoTimestamp result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a value formatted as <c>"yyyy-MM-dd HH:mm:ss[.ffffffffffff]"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoTimestamp result)
    {
        if (!TrinoTemporalText.TryParseTimestamp(s.Trim(), out var date, out var picosecondOfDay))
        {
            result = default;
            return false;
        }

        result = new TrinoTimestamp(date, picosecondOfDay);
        return true;
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoTimestamp left, TrinoTimestamp right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoTimestamp left, TrinoTimestamp right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoTimestamp left, TrinoTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoTimestamp left, TrinoTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoTimestamp left, TrinoTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoTimestamp left, TrinoTimestamp right) => left.CompareTo(right) >= 0;
}
