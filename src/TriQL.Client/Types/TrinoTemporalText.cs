using System.Globalization;

namespace TriQL.Client.Types;

/// <summary>Shared text formatting/parsing for the picosecond-precision temporal types.</summary>
internal static class TrinoTemporalText
{
    private const long PicosecondsPerSecond = TrinoTime.PicosecondsPerSecond;

    public static string FormatTimeOfDay(long picosecondOfDay)
    {
        var secondsOfDay = picosecondOfDay / PicosecondsPerSecond;
        var frac = picosecondOfDay % PicosecondsPerSecond;
        var h = secondsOfDay / 3600;
        var m = secondsOfDay % 3600 / 60;
        var s = secondsOfDay % 60;
        var core = string.Create(CultureInfo.InvariantCulture, $"{h:D2}:{m:D2}:{s:D2}");
        return frac == 0 ? core : core + "." + FormatFraction(frac);
    }

    public static string FormatTimestamp(DateOnly date, long picosecondOfDay) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd} {FormatTimeOfDay(picosecondOfDay)}");

    private static string FormatFraction(long picoseconds)
    {
        var digits = picoseconds.ToString("D12", CultureInfo.InvariantCulture).TrimEnd('0');
        return digits.Length > 0 ? digits : "0";
    }

    public static bool TryParseTimeOfDay(ReadOnlySpan<char> s, out long picosecondOfDay)
    {
        picosecondOfDay = 0;
        if (s.Length < 8 || s[2] != ':' || s[5] != ':')
        {
            return false;
        }

        if (!int.TryParse(s[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(s[3..5], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(s[6..8], NumberStyles.None, CultureInfo.InvariantCulture, out var sec))
        {
            return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59 || sec is < 0 or > 59)
        {
            return false;
        }

        long frac = 0;
        if (s.Length > 8)
        {
            if (s[8] != '.' || !TryParseFraction(s[9..], out frac))
            {
                return false;
            }
        }

        picosecondOfDay = ((long)h * 3600 + m * 60 + sec) * PicosecondsPerSecond + frac;
        return true;
    }

    public static bool TryParseTimestamp(ReadOnlySpan<char> s, out DateOnly date, out long picosecondOfDay)
    {
        date = default;
        picosecondOfDay = 0;
        if (s.Length < 19 || s[10] != ' ')
        {
            return false;
        }

        if (!DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return false;
        }

        return TryParseTimeOfDay(s[11..], out picosecondOfDay);
    }

    /// <summary>Splits a zoned wire value into its local part and trailing zone token (after the last space).</summary>
    public static bool TrySplitZoneSuffix(ReadOnlySpan<char> s, out ReadOnlySpan<char> localPart, out ReadOnlySpan<char> zoneToken)
    {
        var lastSpace = s.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            localPart = default;
            zoneToken = default;
            return false;
        }

        localPart = s[..lastSpace];
        zoneToken = s[(lastSpace + 1)..];
        return true;
    }

    /// <summary>Parses a numeric UTC offset in the form <c>"+HH:MM"</c> or <c>"-HH:MM"</c>.</summary>
    public static bool TryParseNumericOffset(ReadOnlySpan<char> s, out TimeSpan offset)
    {
        offset = default;
        if (s.Length != 6 || (s[0] != '+' && s[0] != '-') || s[3] != ':')
        {
            return false;
        }

        if (!int.TryParse(s[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(s[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        var magnitude = new TimeSpan(hours, minutes, 0);
        offset = s[0] == '-' ? -magnitude : magnitude;
        return true;
    }

    private static bool TryParseFraction(ReadOnlySpan<char> fracDigits, out long picoseconds)
    {
        picoseconds = 0;
        if (fracDigits.Length == 0 || fracDigits.Length > 12)
        {
            return false;
        }

        foreach (var c in fracDigits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        Span<char> padded = stackalloc char[12];
        padded.Fill('0');
        fracDigits.CopyTo(padded);
        return long.TryParse(padded, NumberStyles.None, CultureInfo.InvariantCulture, out picoseconds);
    }
}
