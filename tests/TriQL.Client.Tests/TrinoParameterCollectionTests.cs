using System.Data;

namespace TriQL.Client.Tests;

public sealed class TrinoParameterCollectionTests
{
    [Fact]
    public void Add_PositionalValue_CreatesUnnamedParameter()
    {
        var parameters = new TrinoParameterCollection();

        var parameter = parameters.Add(42);

        Assert.Null(parameter.Name);
        Assert.Equal(42, parameter.Value);
        Assert.Single(parameters);
    }

    [Fact]
    public void Add_NamedValue_CreatesNamedParameter()
    {
        var parameters = new TrinoParameterCollection();

        parameters.Add("id", 7);

        Assert.Equal(7, parameters["id"].Value);
    }

    [Fact]
    public void Indexer_ByName_IsCaseInsensitive()
    {
        var parameters = new TrinoParameterCollection();
        parameters.Add("Id", 7);

        Assert.Equal(7, parameters["ID"].Value);
    }

    [Fact]
    public void Indexer_ByName_UnknownName_Throws()
    {
        var parameters = new TrinoParameterCollection();

        Assert.Throws<ArgumentException>(() => parameters["missing"]);
    }

    [Fact]
    public void TryGetValue_UnknownName_ReturnsFalse()
    {
        var parameters = new TrinoParameterCollection();

        Assert.False(parameters.TryGetValue("missing", out _));
    }

    [Fact]
    public void Direction_SetToNonInput_ThrowsNotSupportedException()
    {
        var parameter = new TrinoParameter();

        Assert.Throws<NotSupportedException>(() => parameter.Direction = ParameterDirection.Output);
    }

    [Fact]
    public void Direction_SetToInput_Succeeds()
    {
        var parameter = new TrinoParameter { Direction = ParameterDirection.Input };

        Assert.Equal(ParameterDirection.Input, parameter.Direction);
    }
}
