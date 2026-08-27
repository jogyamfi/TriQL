using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Numeric widening/narrowing for <see cref="TrinoRow"/>'s typed accessors. Widening always
/// succeeds; narrowing that would lose data throws <see cref="OverflowException"/>. See FR-7.2.4.
/// </summary>
internal static class NumericAccessors
{
    public static sbyte ToSByte(object value) => value switch
    {
        sbyte v => v,
        short v => checked((sbyte)v),
        int v => checked((sbyte)v),
        long v => checked((sbyte)v),
        _ => throw NotConvertible(value, "SByte"),
    };

    public static short ToInt16(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => checked((short)v),
        long v => checked((short)v),
        _ => throw NotConvertible(value, "Int16"),
    };

    public static int ToInt32(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => v,
        long v => checked((int)v),
        _ => throw NotConvertible(value, "Int32"),
    };

    public static long ToInt64(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => v,
        long v => v,
        _ => throw NotConvertible(value, "Int64"),
    };

    public static float ToSingle(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => v,
        long v => v,
        float v => v,
        double v => (float)v,
        _ => throw NotConvertible(value, "Single"),
    };

    public static double ToDouble(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => v,
        long v => v,
        float v => v,
        double v => v,
        _ => throw NotConvertible(value, "Double"),
    };

    public static decimal ToDecimal(object value) => value switch
    {
        sbyte v => v,
        short v => v,
        int v => v,
        long v => v,
        decimal v => v,
        TrinoBigDecimal v => (decimal)v,
        _ => throw NotConvertible(value, "Decimal"),
    };

    private static InvalidCastException NotConvertible(object value, string targetName) =>
        new($"Cannot convert a value of type '{value.GetType()}' to {targetName}.");
}
