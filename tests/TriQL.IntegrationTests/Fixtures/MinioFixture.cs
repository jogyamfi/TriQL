using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Minio;
using Xunit;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// P7-T1: a MinIO Testcontainer with TLS enabled (required — see
/// <see cref="TestCertificateAuthority"/>) and a bucket ready for spooled-segment SSE-C writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>SSE-C and TLS.</b> The Phase 7 task brief asked us to determine whether MinIO enforces TLS
/// for SSE-C operations in practice. It does not: MinIO's SSE-C implementation does not itself
/// inspect the connection's transport security and will happily perform SSE-C encrypt/decrypt over
/// plain HTTP. What forces TLS here is entirely <c>TriQL.Client</c>'s own <c>UriGuard</c> (see the
/// remarks on <see cref="TestCertificateAuthority"/>), not any MinIO-side requirement — a
/// spec-vs-reality note worth folding into requirements.md: FR-5's "the bucket must allow SSE-C
/// operations" is a distinct requirement from transport security, and AWS S3's own SSE-C
/// documentation separately mandates HTTPS for SSE-C requests (since the customer key travels in a
/// request header), which is where that assumption likely originated. MinIO does not enforce it,
/// but AWS S3 does, so a deployment aimed at real AWS S3 would need HTTPS for this reason as well,
/// independent of UriGuard.
/// </para>
/// </remarks>
public sealed class MinioFixture : IAsyncLifetime, IDisposable
{
    private readonly TestCertificateAuthority _ca;
    private readonly MinioContainer _container;
    private readonly string _accessKey = "triql-spooling-access-key";
    private readonly string _secretKey = "triql-spooling-secret-key-0123456789";
    private readonly string _bucketName = "triql-spooling";

    public MinioFixture()
    {
        _ca = TestCertificateAuthority.Create();
        var hostIp = HostAddress.Resolve();

        using var leaf = _ca.IssueServerCertificate(
            "triql-minio",
            dnsNames: ["localhost", "minio"],
            ipAddresses: [hostIp, IPAddress.Loopback]);

        var certPem = TestCertificateAuthority.ToCertificatePem(leaf);
        var keyPem = TestCertificateAuthority.ToPrivateKeyPem(leaf);

        _container = new MinioBuilder("quay.io/minio/minio:latest")
            .WithUsername(_accessKey)
            .WithPassword(_secretKey)
            .WithPortBinding(MinioBuilder.MinioPort, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(certPem), "/root/.minio/certs/public.crt")
            .WithResourceMapping(Encoding.UTF8.GetBytes(keyPem), "/root/.minio/certs/private.key")
            // The bundled wait strategy probes /minio/health/ready over plain HTTP, which no
            // longer answers once certs are present (MinIO serves HTTPS-only on that port).
            // Replace it with a bare port-open check; InitializeAsync does the real, TLS-aware
            // readiness probing below (same pattern as TrinoContainerFixture).
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(MinioBuilder.MinioPort))
            .Build();

        HostIp = hostIp;
    }

    public IPAddress HostIp { get; }

    public string AccessKey => _accessKey;

    public string SecretKey => _secretKey;

    public string BucketName => _bucketName;

    /// <summary>
    /// The bucket's S3 endpoint as reachable both from inside the Trino container (via Docker
    /// Desktop's default bridge networking routing back to the host) and from this bare-host test
    /// process — see <see cref="HostAddress"/>. Valid only after <see cref="InitializeAsync"/>.
    /// </summary>
    public Uri S3Endpoint { get; private set; } = null!;

    /// <summary>The CA that signed MinIO's certificate — Trino's outbound S3 client must trust it (see <see cref="TestCertificateAuthority.Pkcs12TrustStore"/>).</summary>
    public TestCertificateAuthority CertificateAuthority => _ca;

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        var mappedPort = _container.GetMappedPublicPort(MinioBuilder.MinioPort);
        S3Endpoint = new Uri($"https://{HostIp}:{mappedPort}/");

        await WaitUntilReadyAsync().ConfigureAwait(false);
        await CreateBucketAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
        _ca.Dispose();
    }

    public void Dispose() => _ca.Dispose();

    /// <summary>Builds an S3 client that talks to this bucket directly, bypassing TriQL entirely (P7-T7/P7-T8 raw-object verification).</summary>
    public AmazonS3Client CreateS3Client()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = S3Endpoint.ToString(),
            ForcePathStyle = true,
            AuthenticationRegion = "fake-value",
            HttpClientFactory = new TrustingHttpClientFactory(_ca),
        };
        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }

    private async Task WaitUntilReadyAsync()
    {
#pragma warning disable CA2000 // Ownership transfers to the HttpClient constructed on the next line, disposed by the using below.
        using var client = new HttpClient(_ca.CreateTrustingHandler()) { Timeout = TimeSpan.FromSeconds(10) };
#pragma warning restore CA2000

        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(new Uri(S3Endpoint, "minio/health/ready")).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastFailure = new InvalidOperationException($"MinIO health check returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        throw new TimeoutException("MinIO did not become ready in time.", lastFailure);
    }

    private async Task CreateBucketAsync()
    {
        using var client = CreateS3Client();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName, UseClientRegion = true }).ConfigureAwait(false);
    }

    /// <summary>An <see cref="Amazon.Runtime.HttpClientFactory"/> that trusts only the test CA (never system roots), for the S3 client used against MinIO's self-signed certificate.</summary>
    private sealed class TrustingHttpClientFactory(TestCertificateAuthority ca) : Amazon.Runtime.HttpClientFactory
    {
#pragma warning disable CA2000 // Ownership transfers to the returned HttpClient, disposed by the AWS SDK pipeline.
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(ca.CreateTrustingHandler());
#pragma warning restore CA2000
    }
}
