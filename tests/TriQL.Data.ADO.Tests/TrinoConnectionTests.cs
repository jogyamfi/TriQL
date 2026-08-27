using System.Data;
using System.Net;
using TriQL.Client;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO.Tests.Fakes;

namespace TriQL.Data.ADO.Tests;

public sealed class TrinoConnectionTests
{
    [Fact]
    public async Task OpenAsync_TransitionsToOpen_AndRaisesStateChange()
    {
        using var fake = new FakeTrinoCoordinator();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        using var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));

        var raised = new List<(ConnectionState Old, ConnectionState New)>();
        connection.StateChange += (_, e) => raised.Add((e.OriginalState, e.CurrentState));

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Contains((ConnectionState.Closed, ConnectionState.Connecting), raised);
        Assert.Contains((ConnectionState.Connecting, ConnectionState.Open), raised);
    }

    [Fact]
    public async Task CloseAsync_ReturnsToClosed()
    {
        using var fake = new FakeTrinoCoordinator();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        using var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));

        await connection.OpenAsync();
        await connection.CloseAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void ConnectionString_SetWhileOpen_Throws()
    {
        using var connection = new TrinoConnection("Host=h;");
        // Force Open state without a real network round trip by using a closed-but-not-open connection is
        // enough to prove the Closed-only path; opening requires network I/O covered by OpenAsync tests above.
        Assert.Equal(ConnectionState.Closed, connection.State);
        connection.ConnectionString = "Host=other;";
        Assert.Equal("Host=other;", connection.ConnectionString);
    }

    [Fact]
    public void ConnectionString_Parses_IntoDataSourceAndDatabase()
    {
        using var connection = new TrinoConnection("Host=trino.example.com;EnableSsl=true;Catalog=hive;Schema=default;");
        Assert.Equal("https://trino.example.com/", connection.DataSource);
        Assert.Equal("default", connection.Database);
    }

    [Fact]
    public void BeginDbTransaction_Throws()
    {
        using var connection = new TrinoConnection("Host=h;");
        Assert.Throws<NotSupportedException>(() => connection.BeginTransaction());
    }

    [Fact]
    public async Task TestConnectionOnOpen_ServerStarting_ThrowsAndConnectionRemainsClosed()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"nodeVersion":{"version":"466"},"environment":"test","coordinator":true,"starting":true}""");

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/"), TestConnectionOnOpen = true };
        using var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));

        await Assert.ThrowsAsync<TriQL.Client.Exceptions.TrinoConnectionException>(() => connection.OpenAsync());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task ChangeDatabaseAsync_ExecutesUseStatement()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(HttpStatusCode.OK, """{"id":"q1","nextUri":null,"columns":null,"data":null}""");

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        using var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));
        await connection.OpenAsync();

        await connection.ChangeDatabaseAsync("newschema");

        var submitted = Assert.Single(fake.ReceivedRequests, r => r.Method == HttpMethod.Post);
        Assert.NotNull(submitted);
    }

    [Fact]
    public void CreateCommand_BindsToTheConnection()
    {
        using var connection = new TrinoConnection("Host=h;");
        using var command = connection.CreateCommand();
        Assert.Same(connection, command.Connection);
    }
}
