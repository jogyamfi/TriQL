using System.Data;

namespace TriQL.Client;

/// <summary>
/// A single bound parameter for a parameterized statement. Values are rendered into the Trino
/// <c>EXECUTE ... USING</c> clause by the audited <c>SqlLiteralEncoder</c>. See FR-8.7.
/// </summary>
public sealed class TrinoParameter
{
    private ParameterDirection _direction = ParameterDirection.Input;

    /// <summary>Initializes a new, unnamed parameter with a <see langword="null"/> value.</summary>
    public TrinoParameter()
    {
    }

    /// <summary>Initializes a new parameter with the given name and value.</summary>
    public TrinoParameter(string? name, object? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The parameter name, for <c>:name</c>/<c>@name</c> binding. <see langword="null"/> for positional (<c>?</c>) binding.</summary>
    public string? Name { get; set; }

    /// <summary>The parameter's CLR value.</summary>
    public object? Value { get; set; }

    /// <summary>An explicit <see cref="System.Data.DbType"/> hint, used when the CLR value's type alone is ambiguous.</summary>
    public DbType? DbType { get; set; }

    /// <summary>An explicit Trino type name override (e.g. <c>"decimal(38,10)"</c>), rendered via <c>CAST</c>.</summary>
    public string? TrinoType { get; set; }

    /// <summary>An optional precision hint.</summary>
    public byte? Precision { get; set; }

    /// <summary>An optional scale hint.</summary>
    public byte? Scale { get; set; }

    /// <summary>An optional size hint.</summary>
    public int? Size { get; set; }

    /// <summary>Whether the parameter accepts <see langword="null"/>.</summary>
    public bool IsNullable { get; set; }

    /// <summary>
    /// The parameter direction. Only <see cref="ParameterDirection.Input"/> is supported.
    /// </summary>
    /// <exception cref="NotSupportedException">A value other than <see cref="ParameterDirection.Input"/> was set.</exception>
    public ParameterDirection Direction
    {
        get => _direction;
        set
        {
            if (value != ParameterDirection.Input)
            {
                throw new NotSupportedException("TriQL only supports input parameters.");
            }

            _direction = value;
        }
    }
}
