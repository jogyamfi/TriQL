using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>
/// A Trino <c>interval year to month</c> value, expressed as a signed total month count. Not
/// representable as <see cref="TimeSpan"/>. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoIntervalYearToMonth :
    IEquatable<TrinoIntervalYearToMonth>,
    IComparable<TrinoIntervalYearToMonth>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoIntervalYearToMonth>
{
    /// <summary>Initializes a new instance from a total month count.</summary>
    public TrinoIntervalYearToMonth(int totalMonths) => TotalMonths = totalMonths;

    /// <summary>The signed total number of months.</summary>
    public int TotalMonths { get; }

    /// <summary>The whole-year component (truncated toward zero).</summary>
    public int Years => TotalMonths / 12;

    /// <summary>The remaining month component (truncated toward zero).</summary>
    public int Months => TotalMonths % 12;

    /// <summary>Explicit narrowing conversion to the total month count as an <see cref="int"/>.</summary>
    public static explicit operator int(TrinoIntervalYearToMonth value) => value.TotalMonths;

    /// <inheritdoc/>
    public bool Equals(TrinoIntervalYearToMonth other) => TotalMonths == other.TotalMonths;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoIntervalYearToMonth other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => TotalMonths.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(TrinoIntervalYearToMonth other) => TotalMonths.CompareTo(other.TotalMonths);

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var sign = TotalMonths < 0 ? "-" : string.Empty;
        var absYears = Math.Abs(Years);
        var absMonths = Math.Abs(Months);
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{absYears}-{absMonths}");
    }

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
    public static TrinoIntervalYearToMonth Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a value formatted as <c>"[-]Y-M"</c>, e.g. <c>"1-2"</c> for 1 year 2 months.</summary>
    public static TrinoIntervalYearToMonth Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid year-to-month interval.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoIntervalYearToMonth result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a value formatted as <c>"[-]Y-M"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoIntervalYearToMonth result)
    {
        s = s.Trim();
        var negative = false;
        if (s.Length > 0 && s[0] == '-')
        {
            negative = true;
            s = s[1..];
        }

        var dash = s.IndexOf('-');
        if (dash < 0
            || !int.TryParse(s[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var years)
            || !int.TryParse(s[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var months))
        {
            result = default;
            return false;
        }

        var total = years * 12 + months;
        result = new TrinoIntervalYearToMonth(negative ? -total : total);
        return true;
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoIntervalYearToMonth left, TrinoIntervalYearToMonth right) => left.CompareTo(right) >= 0;
}
