using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal.Mapping;

// These are internal APIs that support the EF Core infrastructure and are not subject to the same
// compatibility standards as public APIs.

/// <summary>
/// Maps <see cref="string"/> to <c>varchar</c> (unbounded) or <c>varchar(n)</c> when a maximum
/// length is configured, and to <c>char(n)</c> for fixed-length strings. Trino strings are always
/// Unicode; literals have no <c>N</c> prefix and escape <c>'</c> by doubling it.
/// </summary>
/// <remarks>
/// A value longer than a <c>varchar(n)</c>/<c>char(n)</c> column fails the write rather than being
/// truncated. <c>char(n)</c> values are space-padded by the server and compare equal to their
/// unpadded form.
/// </remarks>
public class TrinoStringTypeMapping : StringTypeMapping
{
    /// <summary>Initializes a new <c>varchar</c> (or, with <paramref name="fixedLength"/>, <c>char</c>) mapping.</summary>
    public TrinoStringTypeMapping(bool fixedLength = false)
        : this(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(string), jsonValueReaderWriter: Microsoft.EntityFrameworkCore.Storage.Json.JsonStringReaderWriter.Instance),
            fixedLength ? "char" : "varchar",
            StoreTypePostfix.Size,
            fixedLength ? System.Data.DbType.StringFixedLength : System.Data.DbType.String,
            unicode: true,
            fixedLength: fixedLength))
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoStringTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoStringTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) => TrinoSqlGenerationHelper.GenerateStringLiteral((string)value);
}

/// <summary>Maps <see cref="byte"/> arrays to <c>varbinary</c>, with <c>X'0A0B'</c> literals.</summary>
public class TrinoByteArrayTypeMapping : ByteArrayTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoByteArrayTypeMapping()
        : base("varbinary", System.Data.DbType.Binary)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoByteArrayTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoByteArrayTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) => "X'" + Convert.ToHexString((byte[])value) + "'";
}

/// <summary>Maps <see cref="Guid"/> to Trino's native <c>uuid</c>, with <c>UUID '…'</c> literals.</summary>
public class TrinoGuidTypeMapping : GuidTypeMapping
{
    /// <summary>Initializes a new instance.</summary>
    public TrinoGuidTypeMapping()
        : base("uuid", System.Data.DbType.Guid)
    {
    }

    /// <summary>Initializes a new instance from mapping parameters.</summary>
    protected TrinoGuidTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new TrinoGuidTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value) => TrinoLiterals.Typed("UUID", ((Guid)value).ToString("D"));
}
