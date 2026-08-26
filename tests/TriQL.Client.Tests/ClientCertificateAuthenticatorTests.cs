using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TriQL.Client.Auth;

namespace TriQL.Client.Tests;

public sealed class ClientCertificateAuthenticatorTests
{
    [Fact]
    public void FromBytes_LoadsCertificate_FromPfxBytes()
    {
        var (pfxBytes, password) = CreateSelfSignedPfx();

        using var authenticator = ClientCertificateAuthenticator.FromBytes(pfxBytes, password);

        AssertAttachesOneCertificate(authenticator);
    }

    [Fact]
    public void FromFile_LoadsCertificate_FromPfxFile()
    {
        var (pfxBytes, password) = CreateSelfSignedPfx();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, pfxBytes);

            using var authenticator = ClientCertificateAuthenticator.FromFile(path, password);

            AssertAttachesOneCertificate(authenticator);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPem_LoadsCertificate_FromPemStrings()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=triql-test-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var certificatePem = certificate.ExportCertificatePem();
        var privateKeyPem = rsa.ExportPkcs8PrivateKeyPem();

        using var authenticator = ClientCertificateAuthenticator.FromPem(certificatePem, privateKeyPem);

        AssertAttachesOneCertificate(authenticator);
    }

    [Fact]
    public async Task ApplyAsync_And_TryRefreshAsync_AreNoOps()
    {
        var (pfxBytes, password) = CreateSelfSignedPfx();
        using var authenticator = ClientCertificateAuthenticator.FromBytes(pfxBytes, password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://trino.example.com/v1/info");
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);

        await authenticator.ApplyAsync(request, CancellationToken.None);
        var refreshed = await authenticator.TryRefreshAsync(response, CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
        Assert.False(refreshed);
    }

    private static void AssertAttachesOneCertificate(ClientCertificateAuthenticator authenticator)
    {
        using var handler = new SocketsHttpHandler();
        authenticator.ConfigureHandler(handler);

        Assert.NotNull(handler.SslOptions.ClientCertificates);
        Assert.Single(handler.SslOptions.ClientCertificates!);
    }

    private static (byte[] PfxBytes, string Password) CreateSelfSignedPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=triql-test-client", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (certificate.Export(X509ContentType.Pfx, "test-password"), "test-password");
    }
}
