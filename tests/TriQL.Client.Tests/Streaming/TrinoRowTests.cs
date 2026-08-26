namespace TriQL.Client.Tests.Streaming;

public sealed class TrinoRowTests
{
    private static readonly IReadOnlyList<TrinoColumn> Columns =
    [
        new TrinoColumn("nationkey", "bigint"),
        new TrinoColumn("name", "varchar"),
    ];

    [Fact]
    public void GetValue_ByOrdinalAndByName_AgreeAndAreCaseInsensitive()
    {
        var row = new TrinoRow(Columns, [1L, "ALGERIA"]);

        Assert.Equal(1L, row.GetValue(0));
        Assert.Equal("ALGERIA", row.GetValue("name"));
        Assert.Equal("ALGERIA", row.GetValue("NAME"));
        Assert.Equal(row.GetValue(1), row["name"]);
    }

    [Fact]
    public void GetOrdinal_ThrowsForUnknownColumn()
    {
        var row = new TrinoRow(Columns, [1L, "ALGERIA"]);

        Assert.Throws<ArgumentException>(() => row.GetOrdinal("does_not_exist"));
    }

    [Fact]
    public void IsDBNull_ReflectsNullValues()
    {
        var row = new TrinoRow(Columns, [null, "ALGERIA"]);

        Assert.True(row.IsDBNull(0));
        Assert.False(row.IsDBNull(1));
    }

    [Fact]
    public void ToArray_ReturnsAnIndependentCopy()
    {
        var row = new TrinoRow(Columns, [1L, "ALGERIA"]);

        var copy = row.ToArray();
        copy[0] = 999L;

        Assert.Equal(1L, row.GetValue(0));
    }

    [Fact]
    public void Clone_ReturnsAnIndependentRowWithTheSameValues()
    {
        var row = new TrinoRow(Columns, [1L, "ALGERIA"]);

        var clone = row.Clone();

        Assert.Equal(row.GetValue(0), clone.GetValue(0));
        Assert.Equal(row.GetValue(1), clone.GetValue(1));
        Assert.NotSame(row, clone);
    }

    [Fact]
    public void FieldCount_MatchesValueCount()
    {
        var row = new TrinoRow(Columns, [1L, "ALGERIA"]);

        Assert.Equal(2, row.FieldCount);
    }
}
