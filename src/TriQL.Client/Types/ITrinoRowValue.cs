namespace TriQL.Client.Types;

/// <summary>
/// A materialized <c>row(...)</c> value, supporting both named and positional field access.
/// See FR-7.2.1.
/// </summary>
public interface ITrinoRowValue
{
    /// <summary>The number of fields in this row.</summary>
    int FieldCount { get; }

    /// <summary>Returns the field value at <paramref name="ordinal"/>.</summary>
    object? this[int ordinal] { get; }

    /// <summary>Returns the value of the field named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">No field with that name exists.</exception>
    object? this[string name] { get; }

    /// <summary>Returns the field name at <paramref name="ordinal"/>, or <see langword="null"/> if anonymous.</summary>
    string? GetName(int ordinal);

    /// <summary>Returns the 0-based ordinal of the field named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">No field with that name exists.</exception>
    int GetOrdinal(string name);
}
