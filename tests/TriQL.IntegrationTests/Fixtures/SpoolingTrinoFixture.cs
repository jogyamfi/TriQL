using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// P7-T2: a spooling-configured Trino coordinator — <c>protocol.spooling.enabled=true</c>, a
/// generated 256-bit base64 shared secret key, and <c>spooling-manager.name=filesystem</c> pointed
/// at the bucket a <see cref="MinioFixture"/> owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this coordinator runs HTTPS at all.</b> This is the part of P7-T2 the task brief did not
/// anticipate: <c>UriGuard</c> requires <c>https</c> unconditionally for both a spooled segment's
/// <c>uri</c> and its <c>ackUri</c> (see the remarks on <see cref="TestCertificateAuthority"/>), and
/// <c>ackUri</c> is coordinator-origin. A plain-HTTP coordinator — the configuration
/// <see cref="TrinoContainerFixture"/> uses — makes every real spooled query fail once a segment is
/// acknowledged, with <c>TrinoProtocolException</c> naming the http scheme. So this fixture, unlike
/// <see cref="TrinoContainerFixture"/>, enables <c>http-server.https.enabled</c> and the tests in
/// <c>Spooling/</c> connect via <see cref="ServerUri"/> (https), not a plain-http URI. Plain HTTP
/// stays enabled alongside HTTPS purely for this fixture's own readiness probing convenience; it is
/// not used by the client under test.
/// </para>
/// <para>
/// <b>Why Trino needs to trust MinIO's certificate.</b> The coordinator's own outbound S3 client
/// (used to actually write segments during query execution, independent of what the eventual
/// client does) must trust MinIO's self-signed leaf certificate or every spool attempt fails
/// server-side with a TLS trust error — invisible to the client, which would simply see the
/// negotiation silently decline to spool (exactly the false-pass risk P7-T5 exists to catch). This
/// is done by pointing the JVM's default trust store at a PKCS12 file containing only
/// <see cref="MinioFixture.CertificateAuthority"/>'s public certificate, via
/// <c>-Djavax.net.ssl.trustStore</c> passed through the universal <c>JAVA_TOOL_OPTIONS</c>
/// environment variable rather than by replacing the image's <c>etc/jvm.config</c> (which carries
/// version-sensitive heap/GC tuning this fixture has no reason to touch or replicate).
/// </para>
/// </remarks>
public sealed class SpoolingTrinoFixture : IAsyncLifetime, IDisposable
{
    /// <summary>The minimum Trino server version TriQL supports (NFR-COMPAT-2) — matches <see cref="TrinoContainerFixture.FloorVersion"/>.</summary>
    public const string FloorVersion = "466";

    private const int HttpPort = 8080;
    private const int HttpsPort = 8443;

    private readonly MinioFixture _minio;
    private readonly TestCertificateAuthority _trinoCa;
    private IContainer _container = null!;

    public SpoolingTrinoFixture(MinioFixture minio)
    {
        _minio = minio;
        _trinoCa = TestCertificateAuthority.Create();
    }

    public string RequestedVersion { get; } = Environment.GetEnvironmentVariable("TRIQL_TEST_TRINO_VERSION") is { Length: > 0 } v
        ? v
        : FloorVersion;

    /// <summary>The coordinator's HTTPS base URI. Valid only after <see cref="InitializeAsync"/>.</summary>
    public Uri ServerUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // P7-T3's dependency ordering: MinIO must already be up so its S3 endpoint and bucket exist
        // before the coordinator's spooling-manager.properties can reference them.
        var hostIp = HostAddress.Resolve();

        using var leaf = _trinoCa.IssueServerCertificate(
            "triql-trino",
            dnsNames: ["localhost"],
            ipAddresses: [IPAddress.Loopback, hostIp]);

        var keystoreBytes = TestCertificateAuthority.ToPkcs12Keystore(leaf);
        var truststoreBytes = _minio.CertificateAuthority.Pkcs12TrustStore();

        var secretKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var configProperties = BuildConfigProperties(secretKeyBase64);
        var spoolingManagerProperties = BuildSpoolingManagerProperties();

        _container = new ContainerBuilder(image: $"trinodb/trino:{RequestedVersion}")
            .WithPortBinding(HttpPort, assignRandomHostPort: true)
            .WithPortBinding(HttpsPort, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(configProperties), "/etc/trino/config.properties")
            .WithResourceMapping(Encoding.UTF8.GetBytes(spoolingManagerProperties), "/etc/trino/spooling-manager.properties")
            .WithResourceMapping(keystoreBytes, "/etc/trino/tls/keystore.p12")
            .WithResourceMapping(truststoreBytes, "/etc/trino/tls/truststore.p12")
            .WithEnvironment(
                "JAVA_TOOL_OPTIONS",
                "-Djavax.net.ssl.trustStore=/etc/trino/tls/truststore.p12 " +
                "-Djavax.net.ssl.trustStoreType=PKCS12 " +
                $"-Djavax.net.ssl.trustStorePassword={TestCertificateAuthority.TrustStorePasswordValue}")
            // Same "starting: false is not the same as queryable" caveat as TrinoContainerFixture —
            // this is the bundled Testcontainers HTTP wait, not the real readiness gate below.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                .ForPath("/v1/info")
                .ForPort(HttpPort)
                .ForResponseMessageMatching(async response =>
                {
                    var info = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
                    return info.TryGetProperty("starting", out var starting) && !starting.GetBoolean();
                })))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);

        var httpsPort = _container.GetMappedPublicPort(HttpsPort);
        ServerUri = new Uri($"https://{_container.Hostname}:{httpsPort}/");

        await WaitUntilQueryableAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
        _trinoCa.Dispose();
    }

    public void Dispose() => _trinoCa.Dispose();

    private static string BuildConfigProperties(string secretKeyBase64) =>
        $"""
        #single node install config
        coordinator=true
        node-scheduler.include-coordinator=true
        discovery.uri=http://localhost:{HttpPort}
        catalog.management=static

        http-server.http.port={HttpPort}
        http-server.https.enabled=true
        http-server.https.port={HttpsPort}
        http-server.https.keystore.path=/etc/trino/tls/keystore.p12
        http-server.https.keystore.key={TestCertificateAuthority.KeystorePasswordValue}

        protocol.spooling.enabled=true
        protocol.spooling.shared-secret-key={secretKeyBase64}
        """;

    private string BuildSpoolingManagerProperties() =>
        $"""
        spooling-manager.name=filesystem
        fs.s3.enabled=true
        fs.location=s3://{_minio.BucketName}/
        s3.endpoint={_minio.S3Endpoint}
        s3.region=fake-value
        s3.aws-access-key={_minio.AccessKey}
        s3.aws-secret-key={_minio.SecretKey}
        s3.path-style-access=true
        """;

    /// <summary>
    /// Same "several consecutive successful probes against a real table" pattern as
    /// <see cref="TrinoContainerFixture.WaitUntilQueryableAsync"/> — node registration can still be
    /// flapping after <c>/v1/info</c> reports ready — but issued over HTTPS with a
    /// certificate-validation callback that accepts anything, since this probe's only purpose is
    /// readiness, not security, and the client under test uses its own TLS policy (see the
    /// type-level remarks).
    /// </summary>
    private async Task WaitUntilQueryableAsync()
    {
#pragma warning disable CA2000 // Ownership transfers to the HttpClient constructed on the next line, disposed via the outer using.
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
#pragma warning restore CA2000
        using var client = new HttpClient(handler) { BaseAddress = ServerUri, Timeout = TimeSpan.FromSeconds(10) };
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

        throw new TimeoutException("Spooling-configured Trino coordinator did not become reliably queryable in time.", lastFailure);
    }

    private static async Task ProbeAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/statement")
        {
            Content = new StringContent("SELECT count(*) FROM tpch.tiny.nation", Encoding.UTF8, "text/plain"),
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
