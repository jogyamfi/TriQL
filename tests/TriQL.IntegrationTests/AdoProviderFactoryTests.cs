using System.Data;
using System.Data.Common;
using TriQL.Data.ADO;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P4-T24: <c>DbProviderFactories.RegisterFactory</c> resolves the Trino provider (FR-9.4.2), and
/// the resolved factory's connection/command/parameter/data-source objects work end to end against
/// a live container — not just that they are the right CLR types (that is already covered by the
/// fake-coordinator unit tests in <c>TriQL.Data.ADO.Tests</c>).
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class AdoProviderFactoryTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task ResolvedFactory_CreatesAConnectionCommandAndParameter_ThatRunARealQuery()
    {
        DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance);

        var resolved = DbProviderFactories.GetFactory(TrinoProviderFactory.InvariantName);
        Assert.Same(TrinoProviderFactory.Instance, resolved);

        var builder = resolved.CreateConnectionStringBuilder()!;
        builder["Server"] = fixture.ServerUri.ToString();

        using var connection = resolved.CreateConnection()!;
        connection.ConnectionString = builder.ToString();
        await connection.OpenAsync();
        Assert.Equal(ConnectionState.Open, connection.State);

        using var command = resolved.CreateCommand()!;
        command.Connection = connection;
        command.CommandText = "SELECT nationkey FROM tpch.tiny.nation WHERE name = ?";

        var parameter = resolved.CreateParameter()!;
        parameter.Value = "ALGERIA";
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        Assert.Equal(0L, result);

        await connection.CloseAsync();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task ResolvedFactory_DataSource_OpensAWorkingConnection()
    {
        DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance);
        var resolved = DbProviderFactories.GetFactory(TrinoProviderFactory.InvariantName);

        var builder = resolved.CreateConnectionStringBuilder()!;
        builder["Server"] = fixture.ServerUri.ToString();

        using var dataSource = resolved.CreateDataSource(builder.ToString())!;
        Assert.IsType<TrinoDataSource>(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM tpch.tiny.region";

        var count = await command.ExecuteScalarAsync();
        Assert.Equal(5L, count);
    }

    [Fact]
    public void RegisterFactory_ResolvesTheDocumentedInvariantName()
    {
        DbProviderFactories.RegisterFactory(TrinoProviderFactory.InvariantName, TrinoProviderFactory.Instance);

        Assert.Equal("TriQL.Data.Trino", TrinoProviderFactory.InvariantName);
        Assert.True(DbProviderFactories.TryGetFactory(TrinoProviderFactory.InvariantName, out var resolved));
        Assert.Same(TrinoProviderFactory.Instance, resolved);
    }
}
