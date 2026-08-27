using TriQL.Client.Types;

namespace TriQL.Client.Tests.Types;

public sealed class TrinoTypeSignatureTests
{
    [Theory]
    [InlineData("boolean", "boolean")]
    [InlineData("bigint", "bigint")]
    [InlineData("varchar", "varchar")]
    public void Parse_SimpleType_HasExpectedBaseName(string typeString, string expectedBaseName)
    {
        var signature = TrinoTypeSignature.Parse(typeString);

        Assert.Equal(expectedBaseName, signature.BaseName);
        Assert.Empty(signature.NumericParameters);
    }

    [Fact]
    public void Parse_VarcharWithLength_ExposesLength()
    {
        var signature = TrinoTypeSignature.Parse("varchar(10)");

        Assert.Equal("varchar", signature.BaseName);
        Assert.Equal(10, signature.Length);
    }

    [Fact]
    public void Parse_Decimal_ExposesPrecisionAndScale()
    {
        var signature = TrinoTypeSignature.Parse("decimal(38,10)");

        Assert.Equal(38, signature.Precision);
        Assert.Equal(10, signature.Scale);
    }

    [Fact]
    public void Parse_DecimalWithOnlyPrecision_DefaultsScaleToZero()
    {
        var signature = TrinoTypeSignature.Parse("decimal(10)");

        Assert.Equal(10, signature.Precision);
        Assert.Equal(0, signature.Scale);
    }

    [Fact]
    public void Parse_TimestampWithTimeZone_SetsWithTimeZoneAndPrecision()
    {
        var signature = TrinoTypeSignature.Parse("timestamp(6) with time zone");

        Assert.Equal("timestamp", signature.BaseName);
        Assert.Equal(6, signature.Precision);
        Assert.True(signature.WithTimeZone);
    }

    [Fact]
    public void Parse_IntervalYearToMonth_SetsIntervalRange()
    {
        var signature = TrinoTypeSignature.Parse("interval year to month");

        Assert.Equal("year to month", signature.IntervalRange);
    }

    [Fact]
    public void Parse_IntervalDayToSecond_SetsIntervalRange()
    {
        var signature = TrinoTypeSignature.Parse("interval day to second");

        Assert.Equal("day to second", signature.IntervalRange);
    }

    [Fact]
    public void Parse_Array_ExposesElementType()
    {
        var signature = TrinoTypeSignature.Parse("array(bigint)");

        Assert.Equal("array", signature.BaseName);
        Assert.Single(signature.TypeArguments);
        Assert.Equal("bigint", signature.TypeArguments[0].BaseName);
    }

    [Fact]
    public void Parse_Map_ExposesKeyAndValueTypes()
    {
        var signature = TrinoTypeSignature.Parse("map(varchar, integer)");

        Assert.Equal("map", signature.BaseName);
        Assert.Equal(2, signature.TypeArguments.Count);
        Assert.Equal("varchar", signature.TypeArguments[0].BaseName);
        Assert.Equal("integer", signature.TypeArguments[1].BaseName);
    }

    [Fact]
    public void Parse_RowWithNamedFields_ExposesFieldNamesAndTypes()
    {
        var signature = TrinoTypeSignature.Parse("row(a bigint, b timestamp(6) with time zone)");

        Assert.Equal("row", signature.BaseName);
        Assert.Equal(2, signature.RowFields.Count);
        Assert.Equal("a", signature.RowFields[0].Name);
        Assert.Equal("bigint", signature.RowFields[0].Type.BaseName);
        Assert.Equal("b", signature.RowFields[1].Name);
        Assert.Equal("timestamp", signature.RowFields[1].Type.BaseName);
        Assert.True(signature.RowFields[1].Type.WithTimeZone);
    }

    [Fact]
    public void Parse_RowWithAnonymousFields_LeavesNameNull()
    {
        var signature = TrinoTypeSignature.Parse("row(bigint, varchar)");

        Assert.Equal(2, signature.RowFields.Count);
        Assert.Null(signature.RowFields[0].Name);
        Assert.Null(signature.RowFields[1].Name);
    }

    [Fact]
    public void Parse_RowWithQuotedFieldName_UnescapesDoubledQuotes()
    {
        var signature = TrinoTypeSignature.Parse("row(\"a\"\"b\" bigint)");

        Assert.Equal("a\"b", signature.RowFields[0].Name);
    }

    [Fact]
    public void Parse_DeeplyNestedType_ParsesTheFullTree()
    {
        var signature = TrinoTypeSignature.Parse("map(varchar, array(row(a bigint, b timestamp(6) with time zone)))");

        Assert.Equal("map", signature.BaseName);
        var arrayType = signature.TypeArguments[1];
        Assert.Equal("array", arrayType.BaseName);
        var rowType = arrayType.TypeArguments[0];
        Assert.Equal("row", rowType.BaseName);
        Assert.Equal(2, rowType.RowFields.Count);
    }

    [Fact]
    public void Parse_CachesByDistinctString()
    {
        var first = TrinoTypeSignature.Parse("bigint");
        var second = TrinoTypeSignature.Parse("bigint");

        Assert.Same(first, second);
    }
}
