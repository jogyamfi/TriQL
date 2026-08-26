namespace TriQL.Client.Tests;

public sealed class TrinoSelectedRoleTests
{
    [Fact]
    public void ToString_RendersNamedRole()
    {
        Assert.Equal("ROLE{admin}", TrinoSelectedRole.Named("admin").ToString());
    }

    [Fact]
    public void ToString_RendersAllAndNone()
    {
        Assert.Equal("ALL", TrinoSelectedRole.All.ToString());
        Assert.Equal("NONE", TrinoSelectedRole.None.ToString());
    }

    [Theory]
    [InlineData("ROLE{admin}")]
    [InlineData("ALL")]
    [InlineData("NONE")]
    [InlineData("all")]
    [InlineData("none")]
    public void Parse_RoundTripsWellFormedValues(string value)
    {
        var parsed = TrinoSelectedRole.Parse(value);

        Assert.NotNull(parsed);
    }

    [Fact]
    public void Parse_ThrowsFormatException_ForUnrecognizedValue()
    {
        Assert.Throws<FormatException>(() => TrinoSelectedRole.Parse("garbage"));
    }

    [Fact]
    public void Parse_ThrowsArgumentNullException_ForNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => TrinoSelectedRole.Parse(null!));
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(TrinoSelectedRole.Named("admin"), TrinoSelectedRole.Named("admin"));
        Assert.NotEqual(TrinoSelectedRole.Named("admin"), TrinoSelectedRole.Named("other"));
        Assert.Equal(TrinoSelectedRole.All, TrinoSelectedRole.All);
    }
}
