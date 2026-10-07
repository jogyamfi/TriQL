using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal.Mapping;

// Trino's numeric types. Literals are typed (TINYINT '5', REAL '1.5', DECIMAL '1.50'): a bare 1.5 is
// decimal(2,1) in Trino, not double, and a bare 5 is integer.
//
// These are internal APIs that support the EF Core infrastructure and are not subject to the same
// compatibility standards as public APIs.

/// <summary>Maps <see cref="bool"/> to <c>boolean</c>, with <c>TRUE</c>/<c>FALSE</c> literals.</summary>
public class TrinoBoolTypeMapping : BoolTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoBoolTypeMapping()
        : base("boolean", System.Data.DbType.Boolean)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoBoolTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoBoolTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) => (bool)value ? "TRUE" : "FALSE";
}

/// <summary>Maps <see cref="sbyte"/> to <c>tinyint</c> (signed in Trino).</summary>
public class TrinoSByteTypeMapping : SByteTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoSByteTypeMapping()
        : base("tinyint", System.Data.DbType.SByte)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoSByteTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoSByteTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("TINYINT", ((sbyte)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="byte"/> to <c>smallint</c>: Trino has no unsigned types, and <c>tinyint</c> is signed.</summary>
public class TrinoByteTypeMapping : ByteTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoByteTypeMapping()
        : base("smallint", System.Data.DbType.Byte)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoByteTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoByteTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("SMALLINT", ((byte)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="short"/> to <c>smallint</c>.</summary>
public class TrinoShortTypeMapping : ShortTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoShortTypeMapping()
        : base("smallint", System.Data.DbType.Int16)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoShortTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoShortTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("SMALLINT", ((short)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="uint"/> to <c>bigint</c>.</summary>
public class TrinoUIntTypeMapping : UIntTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoUIntTypeMapping()
        : base("bigint", System.Data.DbType.UInt32)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoUIntTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoUIntTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("BIGINT", ((uint)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="long"/> to <c>bigint</c>.</summary>
public class TrinoLongTypeMapping : LongTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoLongTypeMapping()
        : base("bigint", System.Data.DbType.Int64)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoLongTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoLongTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("BIGINT", ((long)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="ulong"/> to <c>decimal(20,0)</c>, the narrowest Trino type that holds its range.</summary>
public class TrinoULongTypeMapping : ULongTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoULongTypeMapping()
        : base("decimal(20,0)", System.Data.DbType.UInt64)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoULongTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoULongTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("DECIMAL", ((ulong)value).ToString(CultureInfo.InvariantCulture));
}

/// <summary>Maps <see cref="float"/> to <c>real</c>.</summary>
public class TrinoFloatTypeMapping : FloatTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoFloatTypeMapping()
        : base("real", System.Data.DbType.Single)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoFloatTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoFloatTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var f = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        return TrinoLiterals.FloatingPoint("REAL", f, f.ToString("R", CultureInfo.InvariantCulture));
    }
}

/// <summary>Maps <see cref="double"/> to <c>double</c>.</summary>
public class TrinoDoubleTypeMapping : DoubleTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoDoubleTypeMapping()
        : base("double", System.Data.DbType.Double)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoDoubleTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoDoubleTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return TrinoLiterals.FloatingPoint("DOUBLE", d, d.ToString("R", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Maps <see cref="decimal"/> to <c>decimal(p,s)</c>, defaulting to <c>decimal(18,2)</c> (a bare
/// Trino <c>decimal</c> means <c>decimal(38,0)</c>). Precision is at most 38.
/// </summary>
public class TrinoDecimalTypeMapping : DecimalTypeMapping
{
    /// <summary>The precision used when none is configured.</summary>
    public const int DefaultPrecision = 18;

    /// <summary>The scale used when none is configured.</summary>
    public const int DefaultScale = 2;

    /// <summary>The largest precision Trino supports.</summary>
    public const int MaxPrecision = 38;

    /// <summary>Initializes a new instance.</summary>
    public TrinoDecimalTypeMapping()
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(decimal), jsonValueReaderWriter: Microsoft.EntityFrameworkCore.Storage.Json.JsonDecimalReaderWriter.Instance),
            "decimal",
            StoreTypePostfix.PrecisionAndScale,
            System.Data.DbType.Decimal,
            precision: DefaultPrecision,
            scale: DefaultScale))
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoDecimalTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoDecimalTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) =>
        TrinoLiterals.Typed("DECIMAL", Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
}
