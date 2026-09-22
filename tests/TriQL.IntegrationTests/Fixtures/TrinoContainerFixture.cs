using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// Starts a Trino coordinator container for integration tests, at the version named by the
/// <c>TRIQL_TEST_TRINO_VERSION</c> environment variable (defaulting to <see cref="FloorVersion"/>
/// when unset, e.g. for local runs). Shared across the test collection via
/// <see cref="TrinoContainerCollection"/> so the container is started once per test run, not once
/// per test.
/// </summary>
/// <remarks>
/// Honouring <c>TRIQL_TEST_TRINO_VERSION</c> is what makes the nightly workflow's
/// <c>{466, latest}</c> matrix (TEST-6, NFR-COMPAT-2) actually exercise two different server
/// versions instead of running the floor version twice.
/// </remarks>
public sealed class TrinoContainerFixture : IAsyncLifetime
{
    /// <summary>The minimum Trino server version TriQL supports (NFR-COMPAT-2).</summary>
    public const string FloorVersion = "466";

    /// <summary>
    /// The image tag this fixture was started with — either <see cref="FloorVersion"/>, or
    /// whatever <c>TRIQL_TEST_TRINO_VERSION</c> named (e.g. <c>"latest"</c>). Not necessarily
    /// equal to the numeric version the server reports via <c>/v1/info</c>.
    /// </summary>
    public string RequestedVersion { get; } = Environment.GetEnvironmentVariable("TRIQL_TEST_TRINO_VERSION") is { Length: > 0 } v
        ? v
        : FloorVersion;

    private readonly IContainer _container;

    public TrinoContainerFixture()
    {
        // /v1/info returns 200 well before the coordinator is actually ready to accept queries —
        // it keeps returning "starting": true for some time after the HTTP endpoint comes up.
        // Waiting on HTTP success alone (as Testcontainers' default does) races a coordinator
        // that rejects every query with "Trino server is still initializing". Poll the body too.
        _container = new ContainerBuilder(image: $"trinodb/trino:{RequestedVersion}")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                .ForPath("/v1/info")
                .ForPort(8080)
                .ForResponseMessageMatching(async response =>
                {
                    var info = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
                    return info.TryGetProperty("starting", out var starting) && !starting.GetBoolean();
                })))
            .Build();
    }

    /// <summary>The base URI of the running coordinator, valid only after <see cref="InitializeAsync"/>.</summary>
    public Uri ServerUri => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/");

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        await WaitUntilQueryableAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// <c>starting: false</c> on <c>/v1/info</c> means the HTTP server is up, not that the
    /// single-node worker has registered with cluster discovery yet — a query submitted right
    /// after fails with "No nodes available to run query". Worse, node registration itself can
    /// flap for several seconds (heartbeat/refresh cadence), so a single successful probe query
    /// is not proof either — a scan against a real table can still fail immediately afterwards.
    /// Require several consecutive successful probes, scanning an actual table (not a constant
    /// expression, which can be evaluated without scheduling onto a worker at all), before
    /// declaring the cluster ready.
    /// </summary>
    private async Task WaitUntilQueryableAsync()
    {
        using var client = new HttpClient { BaseAddress = ServerUri, Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        Exception? lastFailure = null;
        var consecutiveSuccesses = 0;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await ProbeAsync(client).ConfigureAwait(false);
                consecutiveSuccesses++;
                if (consecutiveSuccesses >= 3)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lastFailure = ex;
                consecutiveSuccesses = 0;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        throw new TimeoutException("Trino coordinator did not become reliably queryable in time.", lastFailure);
    }

    private static async Task ProbeAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/statement")
        {
            Content = new StringContent("SELECT count(*) FROM tpch.tiny.nation", System.Text.Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Trino-User", "triql-readiness-probe");

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

        var nextUri = body.TryGetProperty("nextUri", out var next) ? next.GetString() : null;
        while (nextUri is not null)
        {
            using var follow = await client.GetAsync(nextUri).ConfigureAwait(false);
            follow.EnsureSuccessStatusCode();
            body = await follow.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

            if (body.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.TryGetProperty("message", out var message) ? message.GetString() : "Query failed.");
            }

            nextUri = body.TryGetProperty("nextUri", out var next2) ? next2.GetString() : null;
        }
    }
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
