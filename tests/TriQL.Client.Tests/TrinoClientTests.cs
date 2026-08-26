namespace TriQL.Client.Tests;

public sealed class TrinoClientTests
{
    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TrinoClient(null!));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenServerIsMissing()
    {
        Assert.Throws<ArgumentException>(() => new TrinoClient(new TrinoSessionOptions()));
    }

    [Fact]
    public void Constructor_WithoutExternalInvoker_BuildsAndOwnsItsOwnHandler()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };

        using var client = new TrinoClient(options);

        Assert.Same(options, client.Options);
        Assert.NotNull(client.Session);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var client = new TrinoClient(options);

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var client = new TrinoClient(options);

        await client.DisposeAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public void Dispose_DoesNotDisposeExternallySuppliedInvoker()
    {
        using var trackingHandler = new TrackingHandler();
        using var invoker = new HttpMessageInvoker(trackingHandler, disposeHandler: false);
        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        var client = new TrinoClient(options, invoker);

        client.Dispose();

        Assert.False(trackingHandler.WasDisposed);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool WasDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
