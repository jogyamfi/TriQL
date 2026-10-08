using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TriQL.Data.ADO;
using TriQL.EntityFrameworkCore.Tests.TestUtilities;

namespace TriQL.EntityFrameworkCore.Tests.Storage;

/// <summary>EF2-T1/EF2-T2: CLR ↔ Trino store type mapping, facets and aliases.</summary>
public sealed class TypeMappingTests
{
    private static readonly IRelationalTypeMappingSource Source = CreateSource();

    private static IRelationalTypeMappingSource CreateSource()
    {
        using var context = new EmptyContext(new DbContextOptionsBuilder<EmptyContext>().UseTrino("Server=https://x/;User=u").Options);
        return context.GetService<IRelationalTypeMappingSource>();
    }

    [Theory]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(sbyte), "tinyint")]
    [InlineData(typeof(byte), "smallint")]
    [InlineData(typeof(short), "smallint")]
    [InlineData(typeof(ushort), "integer")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(uint), "bigint")]
    [InlineData(typeof(long), "bigint")]
    [InlineData(typeof(ulong), "decimal(20,0)")]
    [InlineData(typeof(float), "real")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(decimal), "decimal(18,2)")]
    [InlineData(typeof(string), "varchar")]
    [InlineData(typeof(char), "varchar(1)")]
    [InlineData(typeof(byte[]), "varbinary")]
    [InlineData(typeof(Guid), "uuid")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(TimeOnly), "time(6)")]
    [InlineData(typeof(DateTime), "timestamp(6)")]
    [InlineData(typeof(DateTimeOffset), "timestamp(6) with time zone")]
    [InlineData(typeof(TimeSpan), "bigint")]
    public void ClrType_MapsToTheDefaultStoreType(Type clrType, string storeType)
    {
        Assert.Equal(storeType, Source.FindMapping(clrType)!.StoreType);
    }

    [Theory]
    [InlineData("boolean", typeof(bool), "boolean")]
    [InlineData("TINYINT", typeof(sbyte), "TINYINT")]
    [InlineData("int", typeof(int), "int")]
    [InlineData("integer", typeof(int), "integer")]
    [InlineData("decimal(38,10)", typeof(decimal), "decimal(38,10)")]
    [InlineData("varchar(20)", typeof(string), "varchar(20)")]
    [InlineData("char(5)", typeof(string), "char(5)")]
    [InlineData("varbinary", typeof(byte[]), "varbinary")]
    [InlineData("uuid", typeof(Guid), "uuid")]
    [InlineData("time(3)", typeof(TimeOnly), "time(3)")]
    [InlineData("timestamp(9)", typeof(DateTime), "timestamp(9)")]
    [InlineData("timestamp(3) with time zone", typeof(DateTimeOffset), "timestamp(3) with time zone")]
    public void StoreType_MapsToTheClrType_KeepingItsFacets(string storeType, Type clrType, string expectedStoreType)
    {
        var mapping = Source.FindMapping(storeType);

        Assert.NotNull(mapping);
        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(expectedStoreType, mapping.StoreType);
    }

    [Theory]
    [InlineData("timestamp(3) with time zone", 3)]
    [InlineData("timestamp(9)", 9)]
    [InlineData("time(0)", 0)]

    // Trino's implicit precision for a temporal type declared without one is 3.
    [InlineData("timestamp", 3)]
    [InlineData("time", 3)]
    [InlineData("TIMESTAMP WITH TIME ZONE", 3)]
    public void TemporalStoreType_FacetIsAPrecision_NotALength(string storeType, int precision)
    {
        var mapping = Source.FindMapping(storeType)!;

        Assert.Equal(precision, mapping.Precision);
        Assert.Null(mapping.Size);
    }

    [Theory]
    [InlineData("decimal(39,0)")]
    [InlineData("timestamp(13)")]
    [InlineData("time(6) with time zone")]
    [InlineData("ipaddress")]
    [InlineData("interval day to second")]
    [InlineData("json")]
    public void UnsupportedStoreType_HasNoMapping(string storeType)
    {
        Assert.Null(Source.FindMapping(storeType));
    }

    [Fact]
    public void Facets_FromTheModel_ProduceTheExpectedStoreTypes()
    {
        Assert.Equal("decimal(10,4)", Source.FindMapping(typeof(decimal), storeTypeName: null, precision: 10, scale: 4)!.StoreType);
        Assert.Equal("varchar(50)", Source.FindMapping(typeof(string), storeTypeName: null, size: 50)!.StoreType);
        Assert.Equal("char(3)", Source.FindMapping(typeof(string), storeTypeName: null, size: 3, fixedLength: true)!.StoreType);
        Assert.Equal("timestamp(3)", Source.FindMapping(typeof(DateTime), storeTypeName: null, precision: 3)!.StoreType);
        Assert.Equal("timestamp(0) with time zone", Source.FindMapping(typeof(DateTimeOffset), storeTypeName: null, precision: 0)!.StoreType);
        Assert.Equal("time(9)", Source.FindMapping(typeof(TimeOnly), storeTypeName: null, precision: 9)!.StoreType);
    }

    [Fact]
    public void ModelConfiguration_FlowsIntoColumnTypes()
    {
        using var context = new AllTypesContext(new DbContextOptionsBuilder<AllTypesContext>().UseTrino("Server=https://x/;User=u").Options);
        var entity = context.Model.FindEntityType(typeof(AllTypes))!;

        Assert.Equal("varchar(10)", entity.FindProperty(nameof(AllTypes.ShortString))!.GetColumnType());
        Assert.Equal("char(2)", entity.FindProperty(nameof(AllTypes.FixedString))!.GetColumnType());
        Assert.Equal("decimal(38,10)", entity.FindProperty(nameof(AllTypes.WideDecimal))!.GetColumnType());
        Assert.Equal("timestamp(3)", entity.FindProperty(nameof(AllTypes.MillisecondTimestamp))!.GetColumnType());
        Assert.Equal("integer", entity.FindProperty(nameof(AllTypes.Enum))!.GetColumnType());
    }

    [Theory]
    [InlineData(typeof(bool), DbType.Boolean)]
    [InlineData(typeof(byte), DbType.Byte)]
    [InlineData(typeof(ulong), DbType.UInt64)]
    [InlineData(typeof(decimal), DbType.Decimal)]
    [InlineData(typeof(DateTime), DbType.DateTime)]
    [InlineData(typeof(DateTimeOffset), DbType.DateTimeOffset)]
    [InlineData(typeof(TimeOnly), DbType.Time)]
    [InlineData(typeof(Guid), DbType.Guid)]
    public void CreatedParameters_CarryTheDbType_SoNullsAreTyped(Type clrType, DbType dbType)
    {
        using var command = new TrinoCommand();

        var parameter = Source.FindMapping(clrType)!.CreateParameter(command, "p", value: null, nullable: true);

        Assert.Equal(dbType, parameter.DbType);
        Assert.Equal(DBNull.Value, parameter.Value);
    }
}
