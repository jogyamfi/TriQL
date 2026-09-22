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
    public async Task GetServerInfoAsync_ReturnsAVersionAtOrAboveTheFloor()
    {
        var options = new TrinoSessionOptions { Server = fixture.ServerUri };
        await using var client = new TrinoClient(options);

        var info = await client.GetServerInfoAsync();

        // The fixture may be running the floor version or "latest" (TRIQL_TEST_TRINO_VERSION),
        // so assert the numeric floor rather than an exact match — see TrinoContainerFixture.
        Assert.True(int.TryParse(info.Version, System.Globalization.CultureInfo.InvariantCulture, out var reportedVersion), $"Expected a numeric version, got '{info.Version}'.");
        var floorVersion = int.Parse(TrinoContainerFixture.FloorVersion, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(reportedVersion >= floorVersion, $"Server version {reportedVersion} is below the supported floor {floorVersion}.");
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
