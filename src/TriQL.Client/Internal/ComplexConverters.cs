using System.Globalization;
using System.Net;
using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Materializes <c>array(T)</c> and <c>map(K,V)</c> wire values. See FR-7.2.1.
/// </summary>
/// <remarks>
/// Array elements are given a strongly-typed <c>T[]</c> array for every scalar leaf type via an
/// explicit switch (never <c>Array.CreateInstance</c>/<c>Type.MakeArrayType</c>, which are
/// AOT/trim-incompatible); nested array/map/row elements fall back to <c>object?[]</c>, which the
/// FR-7.2.1 table already anticipates. Maps always use <c>IReadOnlyDictionary&lt;object,object?&gt;</c>
/// — building the full key-type × value-type generic cross product via reflection would likewise
/// require AOT-incompatible <c>Type.MakeGenericType</c>.
/// </remarks>
internal static class ComplexConverters
{
    public static object ConvertArray(object?[] raw, TrinoTypeSignature elementType)
    {
        var converted = new object?[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            converted[i] = TrinoValueConverter.Convert(raw[i], elementType);
        }

        // A null element is only representable in T[] when T is a reference type.
        if (TrinoValueConverter.GetDefaultClrType(elementType).IsValueType && Array.Exists(converted, static v => v is null))
        {
            return converted;
        }

        return ToTypedArray(converted, elementType);
    }

    public static object ConvertMap(Dictionary<string, object?> raw, TrinoTypeSignature keyType, TrinoTypeSignature valueType)
    {
        var result = new Dictionary<object, object?>(raw.Count);
        foreach (var (keyText, rawValue) in raw)
        {
            result[ConvertMapKey(keyText, keyType)] = TrinoValueConverter.Convert(rawValue, valueType);
        }

        return result;
    }

    private static object ToTypedArray(object?[] converted, TrinoTypeSignature elementType) => elementType.BaseName switch
    {
        "boolean" => ToTypedArray<bool>(converted),
        "tinyint" => ToTypedArray<sbyte>(converted),
        "smallint" => ToTypedArray<short>(converted),
        "integer" => ToTypedArray<int>(converted),
        "bigint" => ToTypedArray<long>(converted),
        "real" => ToTypedArray<float>(converted),
        "double" => ToTypedArray<double>(converted),
        "decimal" => TrinoValueConverter.UsesClrDecimal(elementType) ? ToTypedArray<decimal>(converted) : ToTypedArray<TrinoBigDecimal>(converted),
        "varchar" or "char" or "json" => ToTypedArray<string>(converted),
        "varbinary" => ToTypedArray<byte[]>(converted),
        "date" => ToTypedArray<DateOnly>(converted),
        "time" => elementType.WithTimeZone
            ? ToTypedArray<TrinoTimeWithTimeZone>(converted)
            : TrinoValueConverter.UsesClrTemporal(elementType) ? ToTypedArray<TimeOnly>(converted) : ToTypedArray<TrinoTime>(converted),
        "timestamp" => elementType.WithTimeZone
            ? TrinoValueConverter.UsesClrTemporal(elementType) ? ToTypedArray<DateTimeOffset>(converted) : ToTypedArray<TrinoTimestampWithTimeZone>(converted)
            : TrinoValueConverter.UsesClrTemporal(elementType) ? ToTypedArray<DateTime>(converted) : ToTypedArray<TrinoTimestamp>(converted),
        "interval" => elementType.IntervalRange == "year to month" ? ToTypedArray<TrinoIntervalYearToMonth>(converted) : ToTypedArray<TimeSpan>(converted),
        "uuid" => ToTypedArray<Guid>(converted),
        "ipaddress" => ToTypedArray<IPAddress>(converted),
        _ => converted,
    };

    private static T[] ToTypedArray<T>(object?[] converted)
    {
        var result = new T[converted.Length];
        for (var i = 0; i < converted.Length; i++)
        {
            result[i] = (T)converted[i]!;
        }

        return result;
    }
    private static object ConvertMapKey(string keyText, TrinoTypeSignature keyType) => keyType.BaseName switch
    {
        "tinyint" or "smallint" or "integer" or "bigint" => TrinoValueConverter.Convert(long.Parse(keyText, CultureInfo.InvariantCulture), keyType)!,
        "real" or "double" => TrinoValueConverter.Convert(double.Parse(keyText, CultureInfo.InvariantCulture), keyType)!,
        "boolean" => bool.Parse(keyText),
        _ => TrinoValueConverter.Convert(keyText, keyType)!,
    };
}

