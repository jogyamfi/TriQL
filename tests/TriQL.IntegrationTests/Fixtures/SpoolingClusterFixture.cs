using TriQL.Client;
using Xunit;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// P7-T3: composes <see cref="MinioFixture"/> and <see cref="SpoolingTrinoFixture"/> on the network
/// topology both need — MinIO starts first (the coordinator's <c>spooling-manager.properties</c>
/// references its endpoint and bucket at startup), then the spooling-configured coordinator.
/// Shared once per test collection via <see cref="SpoolingClusterCollection"/>, matching
/// <see cref="TrinoContainerFixture"/>'s pattern, since starting two containers per test class
/// (let alone per test) would make the suite unacceptably slow — see the CI-wiring notes on
/// <see cref="SpoolingClusterCollection"/>.
/// </summary>
public sealed class SpoolingClusterFixture : IAsyncLifetime
{
    public SpoolingClusterFixture()
    {
        Minio = new MinioFixture();
        Trino = new SpoolingTrinoFixture(Minio);
    }

    public MinioFixture Minio { get; }

    public SpoolingTrinoFixture Trino { get; }

    /// <summary>The spooling coordinator's HTTPS base URI. Valid only after <see cref="InitializeAsync"/>.</summary>
    public Uri ServerUri => Trino.ServerUri;

    public async Task InitializeAsync()
    {
        await Minio.InitializeAsync().ConfigureAwait(false);
        await Trino.InitializeAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await Trino.DisposeAsync().ConfigureAwait(false);
        await Minio.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the <see cref="TrinoSessionOptions"/> baseline every Lane B test starts from: the
    /// spooling coordinator's HTTPS URI, the full spooled-encoding preference list (the FR-1.1.1
    /// 1.1 default, requested explicitly since 1.0's actual default is empty per FR-5.1.6/G7), and
    /// the permissive TLS policy every test in this suite needs for the same reason —
    /// self-signed certificates on both the coordinator and MinIO (see
    /// <see cref="TestCertificateAuthority"/>'s remarks). Individual tests copy and adjust this
    /// rather than duplicating the TLS boilerplate.
    /// </summary>
    public TrinoSessionOptions CreateBaseOptions()
    {
        var options = new TrinoSessionOptions
        {
            Server = ServerUri,
            QueryDataEncodings = ["json+zstd", "json+lz4", "json"],
        };
        options.Tls.AllowSelfSignedCertificate = true;
        options.Tls.AllowHostNameMismatch = true;
        return options;
    }
}

/// <summary>
/// Declares the xUnit collection that shares one <see cref="SpoolingClusterFixture"/> instance
/// across every test class in the collection, avoiding a two-container start per test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SpoolingClusterCollection : ICollectionFixture<SpoolingClusterFixture>
{
    public const string Name = "Spooling cluster";
}
