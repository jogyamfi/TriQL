using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// Starts a Trino coordinator container at the minimum supported version (466) for integration
/// tests. Shared across the test collection via <see cref="TrinoContainerCollection"/> so the
/// container is started once per test run, not once per test.
/// </summary>
/// <remarks>
/// Currently always starts <see cref="FloorVersion"/>. Parameterizing over the floor/latest
/// matrix (TEST-6, NFR-COMPAT-2) is a Phase 1 task (P1-T21); the nightly workflow already
/// expects a <c>TRIQL_TEST_TRINO_VERSION</c> environment variable for this purpose.
/// </remarks>
public sealed class TrinoContainerFixture : IAsyncLifetime
{
    /// <summary>The minimum Trino server version TriQL supports (NFR-COMPAT-2).</summary>
    public const string FloorVersion = "466";

    private readonly IContainer _container = new ContainerBuilder(image: $"trinodb/trino:{FloorVersion}")
        .WithPortBinding(8080, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/v1/info").ForPort(8080)))
        .Build();

    /// <summary>The base URI of the running coordinator, valid only after <see cref="InitializeAsync"/>.</summary>
    public Uri ServerUri => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/");

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Declares the xUnit collection that shares one <see cref="TrinoContainerFixture"/> instance
/// across every test class in the collection, avoiding a container start per test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TrinoContainerCollection : ICollectionFixture<TrinoContainerFixture>
{
    public const string Name = "Trino container";
}
