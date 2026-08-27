using System.Data.Common;
using TriQL.Client;

namespace TriQL.Data.ADO;

/// <summary>A single column's schema metadata for <see cref="TrinoDataReader.GetColumnSchema"/>. See FR-9.3.11.</summary>
internal sealed class TrinoDbColumn : DbColumn
{
    public TrinoDbColumn(TrinoColumn column, int ordinal)
    {
        ColumnName = column.Name;
        ColumnOrdinal = ordinal;
        DataType = column.ClrType;
        DataTypeName = column.TypeName;
        NumericPrecision = column.TypeSignature.Precision;
        NumericScale = column.TypeSignature.Scale;
        AllowDBNull = true;
        IsKey = false;
        IsUnique = false;
        IsReadOnly = true;
        BaseColumnName = column.Name;
    }
}
