using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace TriQL.EntityFrameworkCore.Storage.Internal.Mapping;

// Trino's date/time types. time and timestamp take a precision (fractional-second digits, 0-12);
// a bare `timestamp`/`time` means precision 3, so the mappings always state one, defaulting to 6
// (microseconds, which Iceberg stores). Literals carry exactly that many digits, because Trino takes
// a literal's precision from its digit count. .NET resolves 100 ns (7 digits), so finer precisions
// render 7 digits.
//
// These are internal APIs that support the EF Core infrastructure and are not subject to the same
// compatibility standards as public APIs.

/// <summary>Maps <see cref="DateOnly"/> to <c>date</c>.</summary>
public class TrinoDateOnlyTypeMapping : DateOnlyTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoDateOnlyTypeMapping()
        : base("date", System.Data.DbType.Date)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoDateOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoDateOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("DATE", ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="TimeOnly"/> to <c>time(p)</c>, defaulting to <c>time(6)</c>.</summary>
public class TrinoTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    /// <summary>The precision used when none is configured.</summary>
    public const int DefaultPrecision = 6;

    /// <summary>Initializes a new instance.</summary>
    public TrinoTimeOnlyTypeMapping()
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(TimeOnly), jsonValueReaderWriter: JsonTimeOnlyReaderWriter.Instance),
            "time",
            StoreTypePostfix.Precision,
            System.Data.DbType.Time,
            precision: DefaultPrecision))
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoTimeOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Time((TimeOnly)value, Precision ?? DefaultPrecision);
}

/// <summary>
/// Maps <see cref="DateTime"/> to <c>timestamp(p)</c>, defaulting to <c>timestamp(6)</c>: a wall-clock
/// value with no time zone. <see cref="DateTime.Kind"/> is not stored; values read back as
/// <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
public class TrinoDateTimeTypeMapping : DateTimeTypeMapping
{
    /// <summary>The precision used when none is configured.</summary>
    public const int DefaultPrecision = 6;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDateTimeTypeMapping()
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(DateTime), jsonValueReaderWriter: JsonDateTimeReaderWriter.Instance),
            "timestamp",
            StoreTypePostfix.Precision,
            System.Data.DbType.DateTime,
            precision: DefaultPrecision))
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoDateTimeTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Timestamp((DateTime)value, Precision ?? DefaultPrecision);
}

/// <summary>
/// Maps <see cref="DateTimeOffset"/> to <c>timestamp(p) with time zone</c>, defaulting to
/// <c>timestamp(6) with time zone</c>. Literals and parameters keep the value's offset. Whether it
/// survives storage depends on the connector: Iceberg stores a UTC instant, so values read back in
/// UTC; the memory connector keeps the offset.
/// </summary>
public class TrinoDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
{
    /// <summary>The store type name without its precision.</summary>
    public const string BaseStoreTypeName = "timestamp with time zone";

    /// <summary>The precision used when none is configured.</summary>
    public const int DefaultPrecision = 6;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDateTimeOffsetTypeMapping()
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(DateTimeOffset), jsonValueReaderWriter: JsonDateTimeOffsetReaderWriter.Instance),
            BaseStoreTypeName,
            StoreTypePostfix.Precision,
            System.Data.DbType.DateTimeOffset,
            precision: DefaultPrecision))
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoDateTimeOffsetTypeMapping(parameters);

    /// <summary>
    /// Places the precision inside the name — <c>timestamp(6) with time zone</c> — rather than after
    /// it. A store type given with its own precision (e.g. from <c>HasColumnType</c>) is kept as written.
    /// </summary>
    protected override string ProcessStoreType(RelationalTypeMappingParameters parameters, string storeType, string storeTypeNameBase) =>
        string.Equals(storeType, storeTypeNameBase, StringComparison.OrdinalIgnoreCase) && parameters.Precision is { } precision
            ? string.Create(CultureInfo.InvariantCulture, $"timestamp({precision}) with time zone")
            : storeType;

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.TimestampWithTimeZone((DateTimeOffset)value, Precision ?? DefaultPrecision);
}
