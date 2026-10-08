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

        // Trino has no unsigned types: each maps to the narrowest signed type that holds its range.
        DbType.Byte => "smallint",
        DbType.UInt16 => "integer",
        DbType.UInt32 => "bigint",
        DbType.UInt64 => "decimal(20,0)",
        DbType.Single => "real",
        DbType.Double => "double",
        DbType.Decimal or DbType.Currency or DbType.VarNumeric => "decimal",
        DbType.String or DbType.AnsiString or DbType.StringFixedLength or DbType.AnsiStringFixedLength => "varchar",
        DbType.Binary => "varbinary",
        DbType.Guid => "uuid",
        DbType.Date => "date",
        // Explicit precision: bare `time`/`timestamp` are time(3)/timestamp(3) in Trino, which would
        // truncate the microseconds SqlLiteralEncoder renders for TimeOnly/DateTime/DateTimeOffset.
        DbType.Time => "time(6)",
        DbType.DateTime or DbType.DateTime2 => "timestamp(6)",
        DbType.DateTimeOffset => "timestamp(6) with time zone",
        _ => null,
    };
}
