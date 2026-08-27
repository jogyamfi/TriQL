using System.Data;
using TriQL.Client.Types;

namespace TriQL.Data.ADO.Internal;

/// <summary>Maps a <see cref="TrinoTypeSignature"/> to the closest <see cref="DbType"/>, for schema metadata only.</summary>
internal static class TrinoDbTypeMapping
{
    public static DbType ToDbType(TrinoTypeSignature type) => type.BaseName switch
    {
        "boolean" => DbType.Boolean,
        "tinyint" => DbType.SByte,
        "smallint" => DbType.Int16,
        "integer" => DbType.Int32,
        "bigint" => DbType.Int64,
        "real" => DbType.Single,
        "double" => DbType.Double,
        "decimal" => DbType.Decimal,
        "varchar" or "char" or "json" => DbType.String,
        "varbinary" => DbType.Binary,
        "uuid" => DbType.Guid,
        "date" => DbType.Date,
        "time" => DbType.Time,
        "timestamp" => type.WithTimeZone ? DbType.DateTimeOffset : DbType.DateTime,
        _ => DbType.Object,
    };
}
