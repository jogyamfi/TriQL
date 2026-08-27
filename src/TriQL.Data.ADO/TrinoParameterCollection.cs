using System.Collections;
using System.Data.Common;

namespace TriQL.Data.ADO;

/// <summary>
/// The full <see cref="DbParameterCollection"/> contract for <see cref="TrinoCommand.Parameters"/>,
/// including indexer-by-name (case-insensitive), <see cref="IndexOf(string)"/>,
/// <see cref="Contains(string)"/>, <see cref="Insert"/>, <see cref="RemoveAt(string)"/>,
/// <see cref="AddRange"/>, and <see cref="CopyTo"/>. See FR-9.4.4.
/// </summary>
#pragma warning disable CA1010 // The required DbParameterCollection base only implements non-generic IList; there is no generic ADO.NET contract to satisfy instead.
public sealed class TrinoParameterCollection : DbParameterCollection
#pragma warning restore CA1010
{
    private readonly List<TrinoDbParameter> _items = [];

    /// <inheritdoc/>
    public override int Count => _items.Count;

    /// <inheritdoc/>
    public override object SyncRoot { get; } = new();

    /// <summary>Appends a new named parameter with the given value.</summary>
    public TrinoDbParameter Add(string parameterName, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        var parameter = new TrinoDbParameter { ParameterName = parameterName, Value = value };
        _items.Add(parameter);
        return parameter;
    }

    /// <inheritdoc/>
    public override int Add(object value)
    {
        _items.Add(RequireTrinoParameter(value));
        return _items.Count - 1;
    }

    /// <inheritdoc/>
    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    /// <inheritdoc/>
    public override void Clear() => _items.Clear();

    /// <inheritdoc/>
    public override bool Contains(object value) => value is TrinoDbParameter p && _items.Contains(p);

    /// <inheritdoc/>
    public override bool Contains(string value) => IndexOf(value) >= 0;

    /// <inheritdoc/>
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    protected override DbParameter GetParameter(int index) => _items[index];

    /// <inheritdoc/>
    protected override DbParameter GetParameter(string parameterName) => _items[RequireIndex(parameterName)];

    /// <inheritdoc/>
    public override int IndexOf(object value) => value is TrinoDbParameter p ? _items.IndexOf(p) : -1;

    /// <inheritdoc/>
    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public override void Insert(int index, object value) => _items.Insert(index, RequireTrinoParameter(value));

    /// <inheritdoc/>
    public override void Remove(object value)
    {
        if (value is TrinoDbParameter p)
        {
            _items.Remove(p);
        }
    }

    /// <inheritdoc/>
    public override void RemoveAt(int index) => _items.RemoveAt(index);

    /// <inheritdoc/>
    public override void RemoveAt(string parameterName) => _items.RemoveAt(RequireIndex(parameterName));

    /// <inheritdoc/>
    protected override void SetParameter(int index, DbParameter value) => _items[index] = RequireTrinoParameter(value);

    /// <inheritdoc/>
    protected override void SetParameter(string parameterName, DbParameter value) => _items[RequireIndex(parameterName)] = RequireTrinoParameter(value);

    private int RequireIndex(string parameterName)
    {
        var index = IndexOf(parameterName);

        // CA2201: IndexOutOfRangeException is reserved by the runtime, but matches the real-world
        // ADO.NET provider convention (e.g. SqlParameterCollection) for an unknown parameter name.
#pragma warning disable CA2201
        return index >= 0 ? index : throw new IndexOutOfRangeException($"No parameter named '{parameterName}' exists.");
#pragma warning restore CA2201
    }

    private static TrinoDbParameter RequireTrinoParameter(object value) =>
        value as TrinoDbParameter ?? throw new InvalidCastException($"Parameter must be a {nameof(TrinoDbParameter)}.");
}
