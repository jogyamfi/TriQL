namespace TriQL.Client.Tests.Streaming;

public sealed class TrinoRowTypedAccessorTests
{
    [Fact]
    public void GetInt64_WidensFromSmallerIntegerColumn()
    {
        var columns = new List<TrinoColumn> { new("id", "smallint") };
        var row = new TrinoRow(columns, [42L]);

        Assert.Equal(42L, row.GetInt64(0));
    }

    [Fact]
    public void GetSByte_NarrowingOverflow_ThrowsOverflowException()
    {
        var columns = new List<TrinoColumn> { new("id", "bigint") };
        var row = new TrinoRow(columns, [500L]);

        Assert.Throws<OverflowException>(() => row.GetSByte(0));
    }

    [Fact]
    public void GetInt32_OnNullColumn_ThrowsInvalidCastException()
    {
        var columns = new List<TrinoColumn> { new("id", "integer") };
        var row = new TrinoRow(columns, [null]);

        Assert.Throws<InvalidCastException>(() => row.GetInt32(0));
    }

    [Fact]
    public void GetDecimal_ReturnsClrDecimalForSmallPrecisionColumn()
    {
        var columns = new List<TrinoColumn> { new("price", "decimal(10,2)") };
        var row = new TrinoRow(columns, ["19.99"]);

        Assert.Equal(19.99m, row.GetDecimal(0));
    }

    [Fact]
    public void GetFieldType_ReturnsDefaultClrTypeForColumn()
    {
        var columns = new List<TrinoColumn> { new("id", "tinyint"), new("name", "varchar") };
        var row = new TrinoRow(columns, [(long)5, "x"]);

        Assert.Equal(typeof(sbyte), row.GetFieldType(0));
        Assert.Equal(typeof(string), row.GetFieldType(1));
    }

    [Fact]
    public void GetValue_MaterializesTinyintAsSByte()
    {
        var columns = new List<TrinoColumn> { new("id", "tinyint") };
        var row = new TrinoRow(columns, [5L]);

        Assert.IsType<sbyte>(row.GetValue(0));
        Assert.Equal((sbyte)5, row.GetValue(0));
    }

    [Fact]
    public void GetFieldValue_Generic_ReturnsTypedValue()
    {
        var columns = new List<TrinoColumn> { new("id", "bigint") };
        var row = new TrinoRow(columns, [7L]);

        Assert.Equal(7L, row.GetFieldValue<long>(0));
    }

    [Fact]
    public void GetFieldValue_Generic_OnNullForNullableTypeParam_ReturnsDefault()
    {
        var columns = new List<TrinoColumn> { new("id", "bigint") };
        var row = new TrinoRow(columns, [null]);

        Assert.Null(row.GetFieldValue<long?>(0));
    }
}
