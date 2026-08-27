using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>
/// A Trino <c>time(p)</c> value where <c>p &gt; 7</c>, exceeding the 100 ns resolution of
/// <see cref="TimeOnly"/>. Stores time-of-day at picosecond resolution. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoTime :
    IEquatable<TrinoTime>,
    IComparable<TrinoTime>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoTime>
{
    internal const long PicosecondsPerSecond = 1_000_000_000_000L;
    private const long PicosecondsPerDay = 86_400L * PicosecondsPerSecond;
    private const long PicosecondsPerTick = 100_000L;

    /// <summary>Initializes a new instance from a picosecond-of-day offset.</summary>
    public TrinoTime(long picosecondOfDay)
    {
        if (picosecondOfDay < 0 || picosecondOfDay >= PicosecondsPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(picosecondOfDay), picosecondOfDay, "Must represent a time within a single day.");
        }

        PicosecondOfDay = picosecondOfDay;
    }

    /// <summary>The time of day expressed in picoseconds since midnight.</summary>
    public long PicosecondOfDay { get; }

    /// <summary>Explicit narrowing conversion to <see cref="TimeOnly"/>. Throws if sub-100 ns precision would be lost.</summary>
    public static explicit operator TimeOnly(TrinoTime value)
    {
        var ticks = Math.DivRem(value.PicosecondOfDay, PicosecondsPerTick, out var remainder);
        if (remainder != 0)
        {
            throw new OverflowException($"'{value}' has sub-100ns precision that cannot be represented by TimeOnly.");
        }

        return new TimeOnly(ticks);
    }

    /// <inheritdoc/>
    public bool Equals(TrinoTime other) => PicosecondOfDay == other.PicosecondOfDay;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoTime other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => PicosecondOfDay.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(TrinoTime other) => PicosecondOfDay.CompareTo(other.PicosecondOfDay);

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) => TrinoTemporalText.FormatTimeOfDay(PicosecondOfDay);

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
    public static TrinoTime Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a value formatted as <c>"HH:mm:ss[.ffffffffffff]"</c>.</summary>
    public static TrinoTime Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid time value.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoTime result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a value formatted as <c>"HH:mm:ss[.ffffffffffff]"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoTime result)
    {
        if (!TrinoTemporalText.TryParseTimeOfDay(s.Trim(), out var picosecondOfDay))
        {
            result = default;
            return false;
        }

        result = new TrinoTime(picosecondOfDay);
        return true;
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoTime left, TrinoTime right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoTime left, TrinoTime right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoTime left, TrinoTime right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoTime left, TrinoTime right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoTime left, TrinoTime right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoTime left, TrinoTime right) => left.CompareTo(right) >= 0;
}
