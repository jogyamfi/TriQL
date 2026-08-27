using System.Text.Json;
using TriQL.Client.Internal;
using TriQL.Client.Types;

namespace TriQL.Client;

/// <summary>
/// A single row of a <see cref="TrinoResultSet"/>. <see cref="GetValue(int)"/> and the typed
/// accessors materialize each column's default CLR type (FR-7.2.1) lazily and cache the result per
/// row instance (FR-7.2.7).
/// </summary>
/// <remarks>
/// A <see cref="TrinoRow"/> instance is documented as valid only until the enumerator's next
/// <c>MoveNextAsync</c> call (FR-6.11); use <see cref="ToArray"/> or <see cref="Clone"/> to retain
/// a row's data beyond that point.
/// </remarks>
public sealed class TrinoRow
{
    private readonly IReadOnlyList<TrinoColumn> _columns;
    private readonly object?[] _rawValues;
    private readonly object?[] _materialized;
    private readonly bool[] _computed;
    private readonly bool _valuesAreDecoded;

    internal TrinoRow(IReadOnlyList<TrinoColumn> columns, object?[] values, bool valuesAreDecoded = false)
    {
        _columns = columns;
        _rawValues = values;
        _materialized = new object?[values.Length];
        _computed = new bool[values.Length];
        _valuesAreDecoded = valuesAreDecoded;
    }

    /// <summary>The number of fields in this row.</summary>
    public int FieldCount => _rawValues.Length;

    /// <summary>Returns the column name at <paramref name="ordinal"/>.</summary>
    public string GetName(int ordinal) => _columns[ordinal].Name;

    /// <summary>Returns the default CLR type for the column at <paramref name="ordinal"/> (FR-7.2.1).</summary>
    public Type GetFieldType(int ordinal) => TrinoValueConverter.GetDefaultClrType(_columns[ordinal].TypeSignature);

    /// <summary>Returns the materialized value at <paramref name="ordinal"/>, or <see langword="null"/> if the value is SQL <c>NULL</c>.</summary>
    public object? GetValue(int ordinal)
    {
        if (!_computed[ordinal])
        {
            _materialized[ordinal] = Materialize(ordinal);
            _computed[ordinal] = true;
        }

        return _materialized[ordinal];
    }

    private object? Materialize(int ordinal)
    {
        var raw = _rawValues[ordinal];
        var type = _columns[ordinal].TypeSignature;

        // Decoded scalars are already final; only the deferred complex types still need converting (FR-7.2.7).
        return _valuesAreDecoded && !TrinoValueConverter.RequiresDeferredMaterialization(type)
            ? raw
            : TrinoValueConverter.Convert(raw, type);
    }

    /// <summary>Returns the materialized value of the column named <paramref name="name"/>.</summary>
    public object? GetValue(string name) => GetValue(GetOrdinal(name));

    /// <summary>Returns the materialized value at <paramref name="ordinal"/>.</summary>
    public object? this[int ordinal] => GetValue(ordinal);

    /// <summary>Returns the materialized value of the column named <paramref name="name"/>.</summary>
    public object? this[string name] => GetValue(name);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// A <c>json</c> (or <c>varchar</c>) column may be requested as <see cref="JsonDocument"/> (FR-7.2.1);
    /// the returned document is owned by the caller and must be disposed.
    /// </remarks>
    /// <exception cref="InvalidCastException">The value is <see langword="null"/> and <typeparamref name="T"/> is not nullable, or the value cannot be cast to <typeparamref name="T"/>.</exception>
    public T GetFieldValue<T>(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidCastException($"Column '{GetName(ordinal)}' is NULL and cannot be returned as non-nullable '{typeof(T)}'.");
        }

        if (value is T typed)
        {
            return typed;
        }

        if (typeof(T) == typeof(JsonDocument) && value is string json)
        {
            return (T)(object)JsonDocument.Parse(json);
        }

        throw new InvalidCastException($"Column '{GetName(ordinal)}' of type '{value.GetType()}' cannot be returned as '{typeof(T)}'.");
    }

    /// <summary>Returns <see langword="true"/> if the value at <paramref name="ordinal"/> is SQL <c>NULL</c>.</summary>
    public bool IsDBNull(int ordinal) => _rawValues[ordinal] is null;

    /// <summary>Returns the 0-based ordinal of the column named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">No column with that name exists.</exception>
    public int GetOrdinal(string name)
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException($"No column named '{name}' exists.", nameof(name));
    }

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="bool"/>.</summary>
    public bool GetBoolean(int ordinal) => RequireNonNull<bool>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="sbyte"/>, widening/narrowing as needed.</summary>
    public sbyte GetSByte(int ordinal) => NumericAccessors.ToSByte(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="short"/>, widening/narrowing as needed.</summary>
    public short GetInt16(int ordinal) => NumericAccessors.ToInt16(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="int"/>, widening/narrowing as needed.</summary>
    public int GetInt32(int ordinal) => NumericAccessors.ToInt32(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="long"/>, widening/narrowing as needed.</summary>
    public long GetInt64(int ordinal) => NumericAccessors.ToInt64(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="float"/>, widening as needed.</summary>
    public float GetFloat(int ordinal) => NumericAccessors.ToSingle(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="double"/>, widening as needed.</summary>
    public double GetDouble(int ordinal) => NumericAccessors.ToDouble(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="decimal"/>.</summary>
    public decimal GetDecimal(int ordinal) => NumericAccessors.ToDecimal(RequireNonNull<object>(ordinal));

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="string"/>.</summary>
    public string GetString(int ordinal) => RequireNonNull<string>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as a <see cref="byte"/> array.</summary>
    public byte[] GetBytes(int ordinal) => RequireNonNull<byte[]>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="Guid"/>.</summary>
    public Guid GetGuid(int ordinal) => RequireNonNull<Guid>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="DateOnly"/>.</summary>
    public DateOnly GetDate(int ordinal) => RequireNonNull<DateOnly>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="TimeOnly"/>.</summary>
    public TimeOnly GetTime(int ordinal) => RequireNonNull<TimeOnly>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="DateTime"/>.</summary>
    public DateTime GetDateTime(int ordinal) => RequireNonNull<DateTime>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as <see cref="DateTimeOffset"/>.</summary>
    public DateTimeOffset GetDateTimeOffset(int ordinal) => RequireNonNull<DateTimeOffset>(ordinal);

    /// <summary>Returns the value at <paramref name="ordinal"/> as an <see cref="ITrinoRowValue"/>.</summary>
    public ITrinoRowValue GetRow(int ordinal) => RequireNonNull<ITrinoRowValue>(ordinal);

    /// <summary>Returns an independent copy of this row's values, safe to retain beyond the next <c>MoveNextAsync</c>.</summary>
    public object?[] ToArray() => (object?[])_rawValues.Clone();

    /// <summary>Returns an independent <see cref="TrinoRow"/> snapshot, safe to retain beyond the next <c>MoveNextAsync</c>.</summary>
    public TrinoRow Clone() => new(_columns, ToArray(), _valuesAreDecoded);

    private T RequireNonNull<T>(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is null)
        {
            throw new InvalidCastException($"Column '{GetName(ordinal)}' is NULL.");
        }

        if (typeof(T) == typeof(object))
        {
            return (T)value;
        }

        return value is T typed ? typed : throw new InvalidCastException($"Column '{GetName(ordinal)}' of type '{value.GetType()}' cannot be returned as '{typeof(T)}'.");
    }
}
