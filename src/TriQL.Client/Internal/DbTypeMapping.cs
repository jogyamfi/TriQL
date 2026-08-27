using System.Data;

namespace TriQL.Client.Internal;

/// <summary>
/// Maps <see cref="System.Data.DbType"/> to a Trino type name, as the documented inverse of
/// FR-7.2 where a unique inverse exists. See FR-8.8.
/// </summary>
internal static class DbTypeMapping
{
    public static string? ToTrinoTypeName(DbType dbType) => dbType switch
    {
        DbType.Boolean => "boolean",
        DbType.SByte => "tinyint",
        DbType.Int16 => "smallint",
        DbType.Int32 => "integer",
        DbType.Int64 => "bigint",
        DbType.Single => "real",
        DbType.Double => "double",
        DbType.Decimal or DbType.Currency or DbType.VarNumeric => "decimal",
        DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength => "varchar",
        DbType.Binary => "varbinary",
        DbType.Guid => "uuid",
        DbType.Date => "date",
        DbType.Time => "time",
        DbType.DateTime or DbType.DateTime2 => "timestamp",
        DbType.DateTimeOffset => "timestamp with time zone",
        _ => null,
    };
}
