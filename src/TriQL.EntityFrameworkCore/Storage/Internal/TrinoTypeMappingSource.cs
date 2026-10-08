using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TriQL.EntityFrameworkCore.Storage.Internal.Mapping;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Maps CLR types to Trino types and back, including facets (<c>decimal(p,s)</c>, <c>varchar(n)</c>,
/// <c>char(n)</c>, <c>time(p)</c>, <c>timestamp(p)</c>, <c>timestamp(p) with time zone</c>). A temporal
/// store type written without a precision has Trino's implicit precision of 3.
/// </summary>
/// <remarks>
/// <para>
/// Trino has no unsigned integers, so <see cref="byte"/>, <see cref="ushort"/>, <see cref="uint"/> and
/// <see cref="ulong"/> map to the narrowest signed type that holds their range. <see cref="char"/> is
/// <c>varchar(1)</c>; <see cref="TimeSpan"/> is stored as <c>bigint</c> ticks, because
/// <c>interval day to second</c> is not a column type on most connectors (Iceberg included).
/// Iceberg silently stores <c>tinyint</c>/<c>smallint</c> as <c>integer</c>, <c>char(n)</c>/<c>varchar(n)</c>
/// as <c>varchar</c>, and <c>timestamp(p)</c> as <c>timestamp(6)</c>; values read back through the same
/// mappings.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class TrinoTypeMappingSource : RelationalTypeMappingSource
{
    /// <summary>The largest <c>time</c>/<c>timestamp</c> precision Trino supports (picoseconds).</summary>
    public const int MaxTemporalPrecision = 12;

    /// <summary>The precision Trino gives a <c>time</c>/<c>timestamp</c> type declared without one.</summary>
    public const int ImplicitTemporalPrecision = 3;

    private const string WithTimeZoneSuffix = " with time zone";

    private static readonly TrinoBoolTypeMapping Boolean = new();
    private static readonly TrinoSByteTypeMapping TinyInt = new();
    private static readonly TrinoByteTypeMapping ByteAsSmallInt = new();
    private static readonly TrinoShortTypeMapping SmallInt = new();
    private static readonly UShortTypeMapping UShortAsInteger = new("integer", System.Data.DbType.UInt16);
    private static readonly IntTypeMapping Integer = new("integer", System.Data.DbType.Int32);
    private static readonly TrinoUIntTypeMapping UIntAsBigInt = new();
    private static readonly TrinoLongTypeMapping BigInt = new();
    private static readonly TrinoULongTypeMapping ULongAsDecimal = new();
    private static readonly TrinoFloatTypeMapping Real = new();
    private static readonly TrinoDoubleTypeMapping Double = new();
    private static readonly TrinoDecimalTypeMapping Decimal = new();
    private static readonly TrinoStringTypeMapping Varchar = new();
    private static readonly TrinoStringTypeMapping Char = new(fixedLength: true);
    private static readonly TrinoByteArrayTypeMapping Varbinary = new();
    private static readonly TrinoGuidTypeMapping Uuid = new();
    private static readonly TrinoDateOnlyTypeMapping Date = new();
    private static readonly TrinoTimeOnlyTypeMapping Time = new();
    private static readonly TrinoDateTimeTypeMapping Timestamp = new();
    private static readonly TrinoDateTimeOffsetTypeMapping TimestampWithTimeZone = new();

    private static readonly RelationalTypeMapping CharAsVarchar =
        (RelationalTypeMapping)new TrinoStringTypeMapping().WithStoreTypeAndSize("varchar(1)", 1).WithComposedConverter(new CharToStringConverter());

    private static readonly RelationalTypeMapping TimeSpanAsTicks =
        (RelationalTypeMapping)BigInt.WithComposedConverter(new TimeSpanToTicksConverter());

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings = new()
    {
        [typeof(bool)] = Boolean,
        [typeof(sbyte)] = TinyInt,
        [typeof(byte)] = ByteAsSmallInt,
        [typeof(short)] = SmallInt,
        [typeof(ushort)] = UShortAsInteger,
        [typeof(int)] = Integer,
        [typeof(uint)] = UIntAsBigInt,
        [typeof(long)] = BigInt,
        [typeof(ulong)] = ULongAsDecimal,
        [typeof(float)] = Real,
        [typeof(double)] = Double,
        [typeof(decimal)] = Decimal,
        [typeof(string)] = Varchar,
        [typeof(char)] = CharAsVarchar,
        [typeof(byte[])] = Varbinary,
        [typeof(Guid)] = Uuid,
        [typeof(DateOnly)] = Date,
        [typeof(TimeOnly)] = Time,
        [typeof(DateTime)] = Timestamp,
        [typeof(DateTimeOffset)] = TimestampWithTimeZone,
        [typeof(TimeSpan)] = TimeSpanAsTicks,
    };

    // Keyed by the store type name without facets; facets such as decimal(10,2) or varchar(50) are
    // applied from the mapping info. Case-insensitive, as Trino's type names are.
    private static readonly Dictionary<string, RelationalTypeMapping> StoreTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boolean"] = Boolean,
        ["tinyint"] = TinyInt,
        ["smallint"] = SmallInt,
        ["integer"] = Integer,
        ["int"] = Integer,
        ["bigint"] = BigInt,
        ["real"] = Real,
        ["double"] = Double,
        ["decimal"] = Decimal,
        ["varchar"] = Varchar,
        ["char"] = Char,
        ["varbinary"] = Varbinary,
        ["uuid"] = Uuid,
        ["date"] = Date,
        ["time"] = Time,
        ["timestamp"] = Timestamp,
        [TrinoDateTimeOffsetTypeMapping.BaseStoreTypeName] = TimestampWithTimeZone,
    };

    /// <summary>Initializes a new instance.</summary>
    public TrinoTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo) =>
        base.FindMapping(mappingInfo) ?? FindRawMapping(mappingInfo)?.WithTypeMappingInfo(mappingInfo);

    /// <summary>
    /// Parses Trino store type names EF's default parser does not understand: the precision inside
    /// <c>timestamp(p) with time zone</c>, and the single facet of <c>time(p)</c>/<c>timestamp(p)</c>,
    /// which is a precision rather than a length.
    /// </summary>
    protected override string? ParseStoreTypeName(
        string? storeTypeName,
        ref bool? unicode,
        ref int? size,
        ref int? precision,
        ref int? scale)
    {
        if (storeTypeName is null)
        {
            return null;
        }

        var trimmed = storeTypeName.Trim();
        var withTimeZone = trimmed.EndsWith(WithTimeZoneSuffix, StringComparison.OrdinalIgnoreCase);
        var withoutSuffix = withTimeZone ? trimmed[..^WithTimeZoneSuffix.Length].TrimEnd() : trimmed;

        var baseName = base.ParseStoreTypeName(withoutSuffix, ref unicode, ref size, ref precision, ref scale);
        if (baseName is not null
            && (baseName.Equals("time", StringComparison.OrdinalIgnoreCase) || baseName.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
            && precision is null)
        {
            // A bare time/timestamp means precision 3 in Trino, not the mapping's default of 6.
            precision = size ?? ImplicitTemporalPrecision;
            size = null;
        }

        return withTimeZone && baseName is not null ? baseName + WithTimeZoneSuffix : baseName;
    }

    private static RelationalTypeMapping? FindRawMapping(RelationalTypeMappingInfo mappingInfo)
    {
        // Report no mapping rather than emit DDL or casts Trino rejects.
        if (mappingInfo.Precision > TrinoDecimalTypeMapping.MaxPrecision
            || (mappingInfo.Precision > MaxTemporalPrecision && IsTemporal(mappingInfo)))
        {
            return null;
        }

        var clrType = mappingInfo.ClrType;

        if (mappingInfo.StoreTypeNameBase is { } storeTypeNameBase
            && StoreTypeMappings.TryGetValue(storeTypeNameBase, out var storeMapping)
            && (clrType is null || storeMapping.ClrType == clrType))
        {
            return storeMapping;
        }

        if (clrType == typeof(string) && mappingInfo.IsFixedLength == true)
        {
            return Char;
        }

        return clrType is not null && ClrTypeMappings.TryGetValue(clrType, out var clrMapping) ? clrMapping : null;
    }

    private static bool IsTemporal(RelationalTypeMappingInfo mappingInfo) =>
        mappingInfo.ClrType == typeof(DateTime) || mappingInfo.ClrType == typeof(DateTimeOffset) || mappingInfo.ClrType == typeof(TimeOnly)
        || mappingInfo.StoreTypeNameBase is "time" or "timestamp" or TrinoDateTimeOffsetTypeMapping.BaseStoreTypeName;
}
