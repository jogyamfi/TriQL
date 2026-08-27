using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>
/// A Trino <c>timestamp(p) with time zone</c> value where <c>p &gt; 7</c>, exceeding the 100 ns
/// resolution of <see cref="DateTimeOffset"/>. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoTimestampWithTimeZone :
    IEquatable<TrinoTimestampWithTimeZone>,
    IComparable<TrinoTimestampWithTimeZone>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoTimestampWithTimeZone>
{
    private const long PicosecondsPerTick = 100_000L;

    /// <summary>Initializes a new instance from a local date/time, a UTC offset, and an optional named zone.</summary>
    public TrinoTimestampWithTimeZone(DateOnly date, long picosecondOfDay, TimeSpan offset, string? zoneId = null)
    {
        Date = date;
        PicosecondOfDay = new TrinoTime(picosecondOfDay).PicosecondOfDay;
        Offset = offset;
        ZoneId = zoneId;
    }

    /// <summary>The local calendar date component.</summary>
    public DateOnly Date { get; }

    /// <summary>The local time-of-day component, in picoseconds since midnight.</summary>
    public long PicosecondOfDay { get; }

    /// <summary>The UTC offset in effect for this instant.</summary>
    public TimeSpan Offset { get; }

    /// <summary>The IANA zone id this value was reported in, if the wire form carried one rather than a bare offset.</summary>
    public string? ZoneId { get; }

    /// <summary>Explicit narrowing conversion to <see cref="DateTimeOffset"/>. Throws on sub-100ns precision loss.</summary>
    public static explicit operator DateTimeOffset(TrinoTimestampWithTimeZone value)
    {
        var ticks = Math.DivRem(value.PicosecondOfDay, PicosecondsPerTick, out var remainder);
        if (remainder != 0)
        {
            throw new OverflowException($"'{value}' has sub-100ns precision that cannot be represented by DateTimeOffset.");
        }

        var local = value.Date.ToDateTime(new TimeOnly(ticks), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, value.Offset);
    }

    /// <inheritdoc/>
    public bool Equals(TrinoTimestampWithTimeZone other) => ToUtcInstantTicks() == other.ToUtcInstantTicks();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoTimestampWithTimeZone other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => ToUtcInstantTicks().GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(TrinoTimestampWithTimeZone other) => ToUtcInstantTicks().CompareTo(other.ToUtcInstantTicks());

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        TrinoTemporalText.FormatTimestamp(Date, PicosecondOfDay) + " " + (ZoneId ?? FormatOffset(Offset));

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
    public static TrinoTimestampWithTimeZone Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a value formatted as <c>"yyyy-MM-dd HH:mm:ss[.ffffffffffff] &lt;zone&gt;"</c>.</summary>
    public static TrinoTimestampWithTimeZone Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid timestamp-with-time-zone value.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoTimestampWithTimeZone result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a value formatted as <c>"yyyy-MM-dd HH:mm:ss[.ffffffffffff] &lt;zone&gt;"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoTimestampWithTimeZone result)
    {
        result = default;
        s = s.Trim();
        if (!TrinoTemporalText.TrySplitZoneSuffix(s, out var localPart, out var zoneToken))
        {
            return false;
        }

        if (!TrinoTemporalText.TryParseTimestamp(localPart, out var date, out var picosecondOfDay))
        {
            return false;
        }

        if (TrinoTemporalText.TryParseNumericOffset(zoneToken, out var offset))
        {
            result = new TrinoTimestampWithTimeZone(date, picosecondOfDay, offset);
            return true;
        }

        var zoneId = zoneToken.ToString();
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            // Truncating to ticks here is safe: this local time only resolves the zone's UTC offset.
            var localDateTime = date.ToDateTime(new TimeOnly(picosecondOfDay / PicosecondsPerTick), DateTimeKind.Unspecified);
            var resolvedOffset = zone.GetUtcOffset(localDateTime);
            result = new TrinoTimestampWithTimeZone(date, picosecondOfDay, resolvedOffset, zoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private long ToUtcInstantTicks()
    {
        var daysTicks = Date.DayNumber * TimeSpan.TicksPerDay;
        var timeTicks = PicosecondOfDay / PicosecondsPerTick;
        return daysTicks + timeTicks - Offset.Ticks;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}");
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoTimestampWithTimeZone left, TrinoTimestampWithTimeZone right) => left.CompareTo(right) >= 0;
}
