using System.Collections;

namespace TriQL.Client;

/// <summary>
/// An ordered collection of <see cref="TrinoParameter"/> bound to a parameterized statement. See FR-8.7.
/// </summary>
public sealed class TrinoParameterCollection : IReadOnlyList<TrinoParameter>
{
    private readonly List<TrinoParameter> _items = [];

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public TrinoParameter this[int index] => _items[index];

    /// <summary>Returns the parameter named <paramref name="name"/> (case-insensitive).</summary>
    /// <exception cref="ArgumentException">No parameter with that name exists.</exception>
    public TrinoParameter this[string name] => TryGetValue(name, out var parameter)
        ? parameter
        : throw new ArgumentException($"No parameter named '{name}' exists.", nameof(name));

    /// <summary>Appends a new unnamed (positional) parameter with the given value.</summary>
    public TrinoParameter Add(object? value)
    {
        var parameter = new TrinoParameter(name: null, value);
        _items.Add(parameter);
        return parameter;
    }

    /// <summary>Appends a new named parameter with the given value.</summary>
    public TrinoParameter Add(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var parameter = new TrinoParameter(name, value);
        _items.Add(parameter);
        return parameter;
    }

    /// <summary>Appends an already-constructed parameter.</summary>
    public void Add(TrinoParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _items.Add(parameter);
    }

    /// <summary>Attempts to find a parameter by name (case-insensitive).</summary>
    public bool TryGetValue(string name, out TrinoParameter parameter)
    {
        foreach (var item in _items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                parameter = item;
                return true;
            }
        }

        parameter = null!;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<TrinoParameter> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
