using TriQL.Client;
using TriQL.IntegrationTests.Fixtures;
using Xunit;

namespace TriQL.IntegrationTests;

/// <summary>
/// P1-T21: <c>/v1/info</c> and <see cref="TrinoClient.TestConnectionAsync"/> against a real,
/// running container. See exit criteria in the implementation plan's Phase 1 section.
/// </summary>
[Collection(TrinoContainerCollection.Name)]
public sealed class ClientConnectionTests(TrinoContainerFixture fixture)
{
    [Fact]
    public async Task GetServerInfoAsync_ReturnsTheFloorVersion()
    {
        var options = new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);

        var info = await client.GetServerInfoAsync();

        Assert.Equal(TrinoContainerFixture.FloorVersion, info.Version);
        Assert.False(info.Starting);
        Assert.True(info.Coordinator);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_ForARunningContainer()
    {
        var options = new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);

        Assert.True(await client.TestConnectionAsync());
    }
}
