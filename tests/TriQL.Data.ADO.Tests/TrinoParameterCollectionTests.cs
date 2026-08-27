using System.Data;

namespace TriQL.Data.ADO.Tests;

public sealed class TrinoParameterCollectionTests
{
    [Fact]
    public void Add_ByNameAndValue_AppendsParameter()
    {
        var collection = new TrinoParameterCollection();
        var parameter = collection.Add("p1", 42);

        Assert.Equal(1, collection.Count);
        Assert.Same(parameter, collection[0]);
        Assert.Equal("p1", parameter.ParameterName);
        Assert.Equal(42, parameter.Value);
    }

    [Fact]
    public void IndexOf_ByName_IsCaseInsensitive()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("Catalog", "hive");

        Assert.Equal(0, collection.IndexOf("catalog"));
        Assert.Equal(0, collection.IndexOf("CATALOG"));
        Assert.Equal(-1, collection.IndexOf("missing"));
    }

    [Fact]
    public void Contains_ByName_ReflectsPresence()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("p1", 1);

        Assert.True(collection.Contains("p1"));
        Assert.False(collection.Contains("p2"));
    }

    [Fact]
    public void Insert_PlacesParameterAtIndex()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("a", 1);
        collection.Add("c", 3);
        collection.Insert(1, new TrinoDbParameter { ParameterName = "b", Value = 2 });

        Assert.Equal(["a", "b", "c"], [.. Enumerable.Range(0, collection.Count).Select(i => ((TrinoDbParameter)collection[i]).ParameterName)]);
    }

    [Fact]
    public void RemoveAt_ByName_RemovesTheParameter()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("p1", 1);
        collection.Add("p2", 2);

        collection.RemoveAt("p1");

        Assert.Equal(1, collection.Count);
        Assert.Equal("p2", ((TrinoDbParameter)collection[0]).ParameterName);
    }

    [Fact]
    public void RemoveAt_UnknownName_ThrowsIndexOutOfRangeException()
    {
        var collection = new TrinoParameterCollection();
        Assert.Throws<IndexOutOfRangeException>(() => collection.RemoveAt("missing"));
    }

    [Fact]
    public void AddRange_AddsEveryParameter()
    {
        var collection = new TrinoParameterCollection();
        collection.AddRange(new object[] { new TrinoDbParameter { ParameterName = "a" }, new TrinoDbParameter { ParameterName = "b" } });

        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void CopyTo_CopiesEveryParameter()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("a", 1);
        collection.Add("b", 2);

        var array = new object[2];
        collection.CopyTo(array, 0);

        Assert.Equal(2, array.Length);
        Assert.All(array, item => Assert.IsType<TrinoDbParameter>(item));
    }

    [Fact]
    public void Clear_RemovesAllParameters()
    {
        var collection = new TrinoParameterCollection();
        collection.Add("a", 1);
        collection.Clear();

        Assert.Equal(0, collection.Count);
    }
}

public sealed class TrinoDbParameterTests
{
    [Fact]
    public void Direction_SetToNonInput_Throws()
    {
        var parameter = new TrinoDbParameter();
        Assert.Throws<NotSupportedException>(() => parameter.Direction = ParameterDirection.Output);
    }

    [Fact]
    public void DbType_TracksWhetherExplicitlySet()
    {
        var parameter = new TrinoDbParameter();
        Assert.False(parameter.HasExplicitDbType);

        parameter.DbType = DbType.Int32;
        Assert.True(parameter.HasExplicitDbType);

        parameter.ResetDbType();
        Assert.False(parameter.HasExplicitDbType);
    }

    [Fact]
    public void ParameterName_NullAssignment_BecomesEmptyString()
    {
        var parameter = new TrinoDbParameter { ParameterName = null! };
        Assert.Equal(string.Empty, parameter.ParameterName);
    }
}
