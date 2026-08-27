using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>
/// A Trino <c>time(p) with time zone</c> value: a time-of-day plus a fixed UTC offset. There is no
/// CLR equivalent for a zoned time-of-day without a calendar date. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoTimeWithTimeZone :
    IEquatable<TrinoTimeWithTimeZone>,
    IComparable<TrinoTimeWithTimeZone>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoTimeWithTimeZone>
{
    /// <summary>Initializes a new instance from a picosecond-of-day offset and a fixed UTC offset.</summary>
    public TrinoTimeWithTimeZone(long picosecondOfDay, TimeSpan offset)
    {
        PicosecondOfDay = new TrinoTime(picosecondOfDay).PicosecondOfDay;
        Offset = offset;
    }

    /// <summary>The local time-of-day component, in picoseconds since midnight.</summary>
    public long PicosecondOfDay { get; }

    /// <summary>The fixed UTC offset.</summary>
    public TimeSpan Offset { get; }

    /// <summary>
    /// Explicit narrowing conversion to <see cref="DateTimeOffset"/>, anchored to <c>0001-01-01</c>
    /// since no calendar date is otherwise available. Throws on sub-100ns precision loss.
    /// </summary>
    public static explicit operator DateTimeOffset(TrinoTimeWithTimeZone value)
    {
        var time = (TimeOnly)new TrinoTime(value.PicosecondOfDay);
        return new DateTimeOffset(DateOnly.MinValue.ToDateTime(time, DateTimeKind.Unspecified), value.Offset);
    }

    /// <inheritdoc/>
    public bool Equals(TrinoTimeWithTimeZone other) => PicosecondOfDay == other.PicosecondOfDay && Offset == other.Offset;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoTimeWithTimeZone other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(PicosecondOfDay, Offset);

    /// <inheritdoc/>
    public int CompareTo(TrinoTimeWithTimeZone other)
    {
        // Compare by the instant they represent (converted to a UTC-relative picosecond offset), not local wall-clock value.
        var leftUtc = PicosecondOfDay - Offset.Ticks * 100_000L;
        var rightUtc = other.PicosecondOfDay - other.Offset.Ticks * 100_000L;
        return leftUtc.CompareTo(rightUtc);
    }

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        TrinoTemporalText.FormatTimeOfDay(PicosecondOfDay) + FormatOffset(Offset);

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
    public static TrinoTimeWithTimeZone Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a value formatted as <c>"HH:mm:ss[.ffffffffffff]+HH:MM"</c>.</summary>
    public static TrinoTimeWithTimeZone Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid time-with-time-zone value.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoTimeWithTimeZone result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a value formatted as <c>"HH:mm:ss[.ffffffffffff]+HH:MM"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoTimeWithTimeZone result)
    {
        result = default;
        s = s.Trim();
        if (s.Length < 6)
        {
            return false;
        }

        var offsetStart = s.Length - 6;
        if (!TrinoTemporalText.TryParseNumericOffset(s[offsetStart..], out var offset)
            || !TrinoTemporalText.TryParseTimeOfDay(s[..offsetStart], out var picosecondOfDay))
        {
            return false;
        }

        result = new TrinoTimeWithTimeZone(picosecondOfDay, offset);
        return true;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}");
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoTimeWithTimeZone left, TrinoTimeWithTimeZone right) => left.CompareTo(right) >= 0;
}
