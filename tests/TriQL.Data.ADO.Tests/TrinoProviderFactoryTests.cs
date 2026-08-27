using System.Data.Common;

namespace TriQL.Data.ADO.Tests;

public sealed class TrinoProviderFactoryTests
{
    [Fact]
    public void Instance_IsASingleton()
    {
        Assert.Same(TrinoProviderFactory.Instance, TrinoProviderFactory.Instance);
    }

    [Fact]
    public void InvariantName_MatchesDocumentedValue() => Assert.Equal("TriQL.Data.Trino", TrinoProviderFactory.InvariantName);

    [Fact]
    public void CapabilityFlags_MatchFR941()
    {
        var factory = TrinoProviderFactory.Instance;
        Assert.False(factory.CanCreateBatch);
        Assert.False(factory.CanCreateCommandBuilder);
        Assert.False(factory.CanCreateDataAdapter);
    }

    [Fact]
    public void CreateConnection_ReturnsTrinoConnection() => Assert.IsType<TrinoConnection>(TrinoProviderFactory.Instance.CreateConnection());

    [Fact]
    public void CreateCommand_ReturnsTrinoCommand() => Assert.IsType<TrinoCommand>(TrinoProviderFactory.Instance.CreateCommand());

    [Fact]
    public void CreateParameter_ReturnsTrinoDbParameter() => Assert.IsType<TrinoDbParameter>(TrinoProviderFactory.Instance.CreateParameter());

    [Fact]
    public void CreateConnectionStringBuilder_ReturnsTrinoConnectionStringBuilder() =>
        Assert.IsType<TrinoConnectionStringBuilder>(TrinoProviderFactory.Instance.CreateConnectionStringBuilder());

    [Fact]
    public void CreateDataSource_ReturnsTrinoDataSource()
    {
        using var dataSource = TrinoProviderFactory.Instance.CreateDataSource("Host=h;");
        Assert.IsType<TrinoDataSource>(dataSource);
    }

    [Fact]
    public void RegisterFactory_ResolvesAndCreatesWorkingConnection()
    {
        DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance);

        var resolved = DbProviderFactories.GetFactory(TrinoProviderFactory.InvariantName);
        using var connection = resolved.CreateConnection();

        Assert.NotNull(connection);
        Assert.IsType<TrinoConnection>(connection);
    }
}
