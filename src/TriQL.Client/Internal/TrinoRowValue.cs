using TriQL.Client.Types;

namespace TriQL.Client.Internal;

/// <summary>
/// Lazily-materializing <see cref="ITrinoRowValue"/> backed by a <c>row(...)</c> type signature and
/// its raw positional field values. See FR-7.2.1, FR-7.2.7.
/// </summary>
internal sealed class TrinoRowValue : ITrinoRowValue
{
    private readonly TrinoTypeSignature _type;
    private readonly object?[] _rawValues;
    private readonly object?[] _materialized;
    private readonly bool[] _computed;

    public TrinoRowValue(TrinoTypeSignature type, object?[] rawValues)
    {
        _type = type;
        _rawValues = rawValues;
        _materialized = new object?[rawValues.Length];
        _computed = new bool[rawValues.Length];
    }

    public int FieldCount => _rawValues.Length;

    public object? this[int ordinal] => GetValue(ordinal);

    public object? this[string name] => GetValue(GetOrdinal(name));

    public string? GetName(int ordinal) => ordinal < _type.RowFields.Count ? _type.RowFields[ordinal].Name : null;

    public int GetOrdinal(string name)
    {
        for (var i = 0; i < _type.RowFields.Count; i++)
        {
            if (string.Equals(_type.RowFields[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentException($"No field named '{name}' exists.", nameof(name));
    }

    private object? GetValue(int ordinal)
    {
        if (!_computed[ordinal])
        {
            _materialized[ordinal] = TrinoValueConverter.Convert(_rawValues[ordinal], _type.RowFields[ordinal].Type);
            _computed[ordinal] = true;
        }

        return _materialized[ordinal];
    }
}
