using System.Collections;

namespace TriQL.Client.Internal;

/// <summary>
/// Estimates the in-memory size of decoded row data for <see cref="PageBuffer"/> accounting.
/// Deliberately measures the <b>decoded payload</b>, never the raw response string length
/// (FR-6.5): estimating from <c>responseStr.Length</c> understates managed memory for wide rows,
/// since JSON escaping and BCL object overhead diverge sharply from wire size.
/// </summary>
internal static class PayloadSizeEstimator
{
    private const long ArrayOverheadBytes = 24;
    private const long StringOverheadBytes = 26;
    private const long ScalarBoxBytes = 24;
    private const long DictionaryEntryOverheadBytes = 48;

    public static long EstimateRows(IReadOnlyList<object?[]> rows)
    {
        long total = 0;
        foreach (var row in rows)
        {
            total += EstimateArray(row);
        }

        return total;
    }

    private static long EstimateArray(object?[] values)
    {
        var total = ArrayOverheadBytes;
        foreach (var value in values)
        {
            total += EstimateValue(value);
        }

        return total;
    }

    private static long EstimateValue(object? value) => value switch
    {
        null => 0,
        string s => StringOverheadBytes + (s.Length * 2L),
        object?[] arr => EstimateArray(arr),
        IDictionary dict => EstimateDictionary(dict),
        _ => ScalarBoxBytes,
    };

    private static long EstimateDictionary(IDictionary dict)
    {
        var total = ArrayOverheadBytes;
        foreach (DictionaryEntry entry in dict)
        {
            total += DictionaryEntryOverheadBytes + EstimateValue(entry.Key) + EstimateValue(entry.Value);
        }

        return total;
    }
}
