using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace TriQL.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Maps CLR types to Trino store types. Phase 1 covers only the scalar types needed to run raw SQL
/// and simple queries; Phase 2 replaces this with the full mapping table, Trino literal forms and
/// store-type facets.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class TrinoTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly BoolTypeMapping Boolean = new("boolean", DbType.Boolean);
    private static readonly IntTypeMapping Integer = new("integer", DbType.Int32);
    private static readonly LongTypeMapping BigInt = new("bigint", DbType.Int64);
    private static readonly DoubleTypeMapping Double = new("double", DbType.Double);
    private static readonly StringTypeMapping Varchar = new("varchar", DbType.String);

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings = new()
    {
        [typeof(bool)] = Boolean,
        [typeof(int)] = Integer,
        [typeof(long)] = BigInt,
        [typeof(double)] = Double,
        [typeof(string)] = Varchar,
    };

    private static readonly Dictionary<string, RelationalTypeMapping> StoreTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boolean"] = Boolean,
        ["integer"] = Integer,
        ["int"] = Integer,
        ["bigint"] = BigInt,
        ["double"] = Double,
        ["varchar"] = Varchar,
    };

    /// <summary>Initializes a new instance.</summary>
    public TrinoTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.StoreTypeName is { } storeTypeName
            && StoreTypeMappings.TryGetValue(mappingInfo.StoreTypeNameBase ?? storeTypeName, out var byStoreType)
            && (mappingInfo.ClrType is null || mappingInfo.ClrType == byStoreType.ClrType))
        {
            return byStoreType;
        }

        if (mappingInfo.ClrType is { } clrType && ClrTypeMappings.TryGetValue(clrType, out var byClrType))
        {
            return byClrType;
        }

        return base.FindMapping(mappingInfo);
    }
}
