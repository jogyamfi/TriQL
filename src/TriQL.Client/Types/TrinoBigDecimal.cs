using System.Globalization;
using System.Numerics;

namespace TriQL.Client.Types;

/// <summary>
/// An arbitrary-precision decimal value for <c>decimal(p,s)</c> columns where <c>p &gt; 28</c>,
/// beyond what the BCL <see cref="decimal"/> can represent losslessly. See FR-7.2.1, FR-7.2.3.
/// </summary>
public readonly struct TrinoBigDecimal :
    IEquatable<TrinoBigDecimal>,
    IComparable<TrinoBigDecimal>,
    IFormattable,
    ISpanFormattable,
    IParsable<TrinoBigDecimal>
{
    /// <summary>Initializes a new instance from an unscaled integer value and a decimal scale.</summary>
    public TrinoBigDecimal(BigInteger unscaledValue, int scale)
    {
        if (scale < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must not be negative.");
        }

        UnscaledValue = unscaledValue;
        Scale = scale;
    }

    /// <summary>The value's digits, ignoring the decimal point (e.g. <c>12345</c> for <c>123.45</c>).</summary>
    public BigInteger UnscaledValue { get; }

    /// <summary>The number of digits after the decimal point.</summary>
    public int Scale { get; }

    /// <summary>Explicit narrowing conversion to <see cref="decimal"/>. Throws on precision or magnitude loss.</summary>
    public static explicit operator decimal(TrinoBigDecimal value)
    {
        var text = value.ToString(null, CultureInfo.InvariantCulture);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            && TryParse(result.ToString(CultureInfo.InvariantCulture), null, out var roundTrip)
            && roundTrip.CompareTo(value) == 0)
        {
            return result;
        }

        throw new OverflowException($"'{value}' cannot be converted to decimal without loss.");
    }

    /// <inheritdoc/>
    public bool Equals(TrinoBigDecimal other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrinoBigDecimal other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Must hash the trailing-zero-normalized form, since Equals treats 1.0 and 1.00 as equal.
        var (unscaled, scale) = Normalize();
        return HashCode.Combine(unscaled, scale);
    }

    /// <inheritdoc/>
    public int CompareTo(TrinoBigDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        var left = UnscaledValue * BigInteger.Pow(10, scale - Scale);
        var right = other.UnscaledValue * BigInteger.Pow(10, scale - other.Scale);
        return left.CompareTo(right);
    }

    /// <inheritdoc/>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var sign = UnscaledValue.Sign < 0 ? "-" : string.Empty;
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture);
        if (Scale == 0)
        {
            return sign + digits;
        }

        if (digits.Length <= Scale)
        {
            digits = digits.PadLeft(Scale + 1, '0');
        }

        var splitAt = digits.Length - Scale;
        return $"{sign}{digits[..splitAt]}.{digits[splitAt..]}";
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
    public static TrinoBigDecimal Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan());

    /// <summary>Parses a decimal literal such as <c>"123.4500"</c> or <c>"-1"</c>.</summary>
    public static TrinoBigDecimal Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, null, out var result))
        {
            throw new FormatException($"'{s}' is not a valid decimal value.");
        }

        return result;
    }

    /// <inheritdoc/>
    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out TrinoBigDecimal result) =>
        TryParse(s.AsSpan(), provider, out result);

    /// <summary>Attempts to parse a decimal literal such as <c>"123.4500"</c> or <c>"-1"</c>.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TrinoBigDecimal result)
    {
        s = s.Trim();
        var negative = false;
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
        {
            negative = s[0] == '-';
            s = s[1..];
        }

        var dot = s.IndexOf('.');
        ReadOnlySpan<char> digitsSpan;
        int scale;
        if (dot < 0)
        {
            digitsSpan = s;
            scale = 0;
        }
        else
        {
            scale = s.Length - dot - 1;
            var buffer = new char[s.Length - 1];
            s[..dot].CopyTo(buffer);
            s[(dot + 1)..].CopyTo(buffer.AsSpan(dot));
            digitsSpan = buffer;
        }

        if (digitsSpan.Length == 0 || !BigInteger.TryParse(digitsSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var unscaled))
        {
            result = default;
            return false;
        }

        result = new TrinoBigDecimal(negative ? -unscaled : unscaled, scale);
        return true;
    }

    private (BigInteger Unscaled, int Scale) Normalize()
    {
        var unscaled = UnscaledValue;
        var scale = Scale;
        while (scale > 0 && !unscaled.IsZero && BigInteger.Remainder(unscaled, 10).IsZero)
        {
            unscaled /= 10;
            scale--;
        }

        return (unscaled, unscaled.IsZero ? 0 : scale);
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TrinoBigDecimal left, TrinoBigDecimal right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TrinoBigDecimal left, TrinoBigDecimal right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(TrinoBigDecimal left, TrinoBigDecimal right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(TrinoBigDecimal left, TrinoBigDecimal right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(TrinoBigDecimal left, TrinoBigDecimal right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(TrinoBigDecimal left, TrinoBigDecimal right) => left.CompareTo(right) >= 0;
}
