using TriQL.Client.Tests.Fakes;

namespace TriQL.Client.Tests;

public sealed class InfoClientTests
{
    private static string InfoJson(string version, bool starting, bool coordinator = true) =>
        $$"""
        {"nodeVersion":{"version":"{{version}}"},"environment":"test","coordinator":{{(coordinator ? "true" : "false")}},"starting":{{(starting ? "true" : "false")}},"uptime":"1.00m"}
        """;

    [Fact]
    public async Task GetServerInfoAsync_ParsesVersionAndStartingFlag()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson("466", starting: false));
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        var info = await client.GetServerInfoAsync();

        Assert.Equal("466", info.Version);
        Assert.False(info.Starting);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenServerIsStarting()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson("466", starting: true));
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        Assert.False(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_WhenServerIsReady()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson("466", starting: false));
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        Assert.True(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_OnNonSuccessStatus()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.InternalServerError);
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        Assert.False(await client.TestConnectionAsync());
    }

    [Fact]
    public async Task GetServerInfoAsync_SendsRequestToVersionedInfoEndpoint()
    {
        using var fake = new FakeTrinoCoordinator();
        fake.Enqueue(System.Net.HttpStatusCode.OK, InfoJson("466", starting: false));
        using var invoker = fake.CreateInvoker();

        var options = new TrinoSessionOptions { Server = new Uri("https://trino.example.com/") };
        await using var client = new TrinoClient(options, invoker);

        await client.GetServerInfoAsync();

        var request = Assert.Single(fake.ReceivedRequests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/info", request.RequestUri?.AbsolutePath);
    }
}
