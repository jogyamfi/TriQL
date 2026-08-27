using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace TriQL.Data.ADO;

/// <summary>
/// A single ADO.NET parameter bound to a <see cref="TrinoCommand"/>. See FR-9.2.9, FR-9.4.4.
/// </summary>
public sealed class TrinoDbParameter : DbParameter
{
    private DbType _dbType;
    private ParameterDirection _direction = ParameterDirection.Input;
    private string _parameterName = string.Empty;
    private string _sourceColumn = string.Empty;

    /// <inheritdoc/>
    public override DbType DbType
    {
        get => _dbType;
        set
        {
            _dbType = value;
            HasExplicitDbType = true;
        }
    }

    /// <summary>Whether <see cref="DbType"/> was explicitly set, as opposed to defaulted.</summary>
    internal bool HasExplicitDbType { get; private set; }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">A value other than <see cref="ParameterDirection.Input"/> was set.</exception>
    public override ParameterDirection Direction
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

    /// <inheritdoc/>
    public override bool IsNullable { get; set; }

    /// <inheritdoc/>
    [AllowNull]
    public override string ParameterName
    {
        get => _parameterName;
        [MemberNotNull(nameof(_parameterName))]
        set => _parameterName = value ?? string.Empty;
    }

    /// <inheritdoc/>
    public override int Size { get; set; }

    /// <inheritdoc/>
    [AllowNull]
    public override string SourceColumn
    {
        get => _sourceColumn;
        [MemberNotNull(nameof(_sourceColumn))]
        set => _sourceColumn = value ?? string.Empty;
    }

    /// <inheritdoc/>
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc/>
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

    /// <inheritdoc/>
    public override object? Value { get; set; }

    /// <summary>An optional numeric precision hint.</summary>
    public new byte Precision { get; set; }

    /// <summary>An optional numeric scale hint.</summary>
    public new byte Scale { get; set; }

    /// <summary>An explicit Trino type name override (e.g. <c>"decimal(38,10)"</c>), rendered via <c>CAST</c>.</summary>
    public string? TrinoType { get; set; }

    /// <inheritdoc/>
    public override void ResetDbType()
    {
        _dbType = default;
        HasExplicitDbType = false;
    }
}
