using System.Text.Json;

namespace TriQL.Client.Internal;

/// <summary>
/// Decodes raw <c>data</c> row arrays into <see cref="object"/> graphs without any Trino-specific
/// type knowledge. This is a Phase 2 placeholder — full Trino-to-CLR materialization (FR-7.2)
/// arrives in Phase 3 and will replace this conversion for scalar/temporal/decimal columns.
/// </summary>
internal static class RawJsonValueConverter
{
    public static object?[] ConvertRow(JsonElement rowArray)
    {
        var values = new object?[rowArray.GetArrayLength()];
        var i = 0;
        foreach (var element in rowArray.EnumerateArray())
        {
            values[i++] = Convert(element);
        }

        return values;
    }

    public static object? Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ConvertNumber(element),
        JsonValueKind.Array => ConvertRow(element),
        JsonValueKind.Object => ConvertObject(element),
        _ => null,
    };

    private static object ConvertNumber(JsonElement element)
    {
        // NB: a ternary here would unify the long/double branch types to double, silently
        // discarding integer precision even when TryGetInt64 succeeds — hence the if/else.
        if (element.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        return element.GetDouble();
    }

    private static Dictionary<string, object?> ConvertObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = Convert(property.Value);
        }

        return result;
    }
}
