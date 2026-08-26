namespace TriQL.Client;

/// <summary>
/// A single row of a <see cref="TrinoResultSet"/>. Values are currently exposed as raw decoded
/// <see cref="object"/> instances (booleans, strings, <see cref="long"/>/<see cref="double"/> for
/// numbers, nested arrays/dictionaries) — full Trino-to-CLR type materialization (FR-7.2) arrives
/// in Phase 3.
/// </summary>
/// <remarks>
/// A <see cref="TrinoRow"/> instance is documented as valid only until the enumerator's next
/// <c>MoveNextAsync</c> call (FR-6.11); use <see cref="ToArray"/> or <see cref="Clone"/> to retain
/// a row's data beyond that point.
/// </remarks>
public sealed class TrinoRow
{
    private readonly IReadOnlyList<TrinoColumn> _columns;
    private readonly object?[] _values;

    internal TrinoRow(IReadOnlyList<TrinoColumn> columns, object?[] values)
    {
        _columns = columns;
        _values = values;
    }

    /// <summary>The number of fields in this row.</summary>
    public int FieldCount => _values.Length;

    /// <summary>Returns the column name at <paramref name="ordinal"/>.</summary>
    public string GetName(int ordinal) => _columns[ordinal].Name;

    /// <summary>Returns the raw decoded value at <paramref name="ordinal"/>, or <see langword="null"/> if the value is SQL <c>NULL</c>.</summary>
    public object? GetValue(int ordinal) => _values[ordinal];

    /// <summary>Returns the raw decoded value of the column named <paramref name="name"/>.</summary>
    public object? GetValue(string name) => GetValue(GetOrdinal(name));

    /// <summary>Returns the raw decoded value at <paramref name="ordinal"/>.</summary>
    public object? this[int ordinal] => GetValue(ordinal);

    /// <summary>Returns the raw decoded value of the column named <paramref name="name"/>.</summary>
    public object? this[string name] => GetValue(name);

    /// <summary>Returns <see langword="true"/> if the value at <paramref name="ordinal"/> is SQL <c>NULL</c>.</summary>
    public bool IsDBNull(int ordinal) => _values[ordinal] is null;

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

    /// <summary>Returns an independent copy of this row's values, safe to retain beyond the next <c>MoveNextAsync</c>.</summary>
    public object?[] ToArray() => (object?[])_values.Clone();

    /// <summary>Returns an independent <see cref="TrinoRow"/> snapshot, safe to retain beyond the next <c>MoveNextAsync</c>.</summary>
    public TrinoRow Clone() => new(_columns, ToArray());
}
