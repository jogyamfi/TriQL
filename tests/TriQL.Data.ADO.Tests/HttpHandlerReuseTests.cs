using TriQL.Client;
using TriQL.Client.Tests.Fakes;
using TriQL.Data.ADO.Tests.Fakes;

namespace TriQL.Data.ADO.Tests;

/// <summary>
/// EF0-T2: a connection reuses its HTTP handler across Open/Close, and a data source shares one
/// handler across its connections, so ORMs that open and close per command keep pooled TCP/TLS
/// connections. Open() without TestConnection makes no network call, so no server is needed.
/// </summary>
public sealed class HttpHandlerReuseTests
{
    private const string ConnectionString = "Server=https://trino.example.com/;User=test";

    private static async Task AssertDisposedAsync(HttpMessageInvoker invoker)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invoker.SendAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Connection_ReusesItsHandlerAcrossOpenAndClose()
    {
        using var connection = new TrinoConnection(ConnectionString);

        await connection.OpenAsync();
        var first = connection.CurrentInvoker;
        await connection.CloseAsync();
        await connection.OpenAsync();

        Assert.NotNull(first);
        Assert.Same(first, connection.CurrentInvoker);
    }

    [Fact]
    public async Task Connection_ChangingConnectionString_DisposesTheOldHandlerAndBuildsANewOne()
    {
        using var connection = new TrinoConnection(ConnectionString);
        await connection.OpenAsync();
        var first = connection.CurrentInvoker!;
        await connection.CloseAsync();

        connection.ConnectionString = "Server=https://other.example.com/;User=test";
        await connection.OpenAsync();

        Assert.NotSame(first, connection.CurrentInvoker);
        await AssertDisposedAsync(first);
    }

    [Fact]
    public async Task Connection_Dispose_DisposesItsHandler()
    {
        var connection = new TrinoConnection(ConnectionString);
        await connection.OpenAsync();
        var invoker = connection.CurrentInvoker!;

        await connection.DisposeAsync();

        await AssertDisposedAsync(invoker);
    }

    [Fact]
    public async Task Connection_WithHttpClientFactory_DoesNotBuildItsOwnHandler()
    {
        using var fake = new FakeTrinoCoordinator();
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        using var connection = new TrinoConnection(options, new StubHttpClientFactory(fake));

        await connection.OpenAsync();

        Assert.Null(connection.CurrentInvoker);
    }

    [Fact]
    public async Task DataSource_ConnectionsShareOneHandler_DisposedWithTheDataSource()
    {
        var dataSource = new TrinoDataSource(ConnectionString);
        await using var first = (TrinoConnection)await dataSource.OpenConnectionAsync();
        await using var second = (TrinoConnection)await dataSource.OpenConnectionAsync();

        var shared = first.CurrentInvoker!;
        Assert.Same(shared, second.CurrentInvoker);

        // Disposing a connection must not dispose the data source's shared handler.
        await first.DisposeAsync();
        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1:1/"))
        {
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => shared.SendAsync(request, CancellationToken.None));
        }

        await dataSource.DisposeAsync();
        await AssertDisposedAsync(shared);
    }
}
