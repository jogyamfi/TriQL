using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TriQL.Client.Internal;

namespace TriQL.Client.Tests;

public sealed class CertificateValidatorTests
{
    [Fact]
    public void Create_RejectsUnpermittedSslPolicyErrors()
    {
        var callback = CertificateValidator.Create(new TrinoTlsOptions(), logger: null);
        using var certificate = CreateSelfSignedCertificate();

        var result = callback(this, certificate, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch);

        Assert.False(result);
    }

    [Fact]
    public void Create_NeverAcceptsBlanketly_WhenMultipleErrorsPresentAndOnlyOneIsPermitted()
    {
        var options = new TrinoTlsOptions { AllowHostNameMismatch = true };
        var callback = CertificateValidator.Create(options, logger: null);
        using var certificate = CreateSelfSignedCertificate();

        // NameMismatch is permitted, but NotAvailable is not: the combination must still be rejected.
        var result = callback(
            this, certificate, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable);

        Assert.False(result);
    }

    [Fact]
    public void Create_AcceptsHostNameMismatch_WhenExplicitlyAllowed()
    {
        var options = new TrinoTlsOptions { AllowHostNameMismatch = true };
        var callback = CertificateValidator.Create(options, logger: null);
        using var certificate = CreateSelfSignedCertificate();

        var result = callback(this, certificate, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch);

        Assert.True(result);
    }

    [Fact]
    public void Create_RejectsSelfSignedChain_WhenNotExplicitlyAllowed()
    {
        var options = new TrinoTlsOptions();
        var callback = CertificateValidator.Create(options, logger: null);
        using var certificate = CreateSelfSignedCertificate();
        using var chain = BuildChainFor(certificate);

        var result = callback(this, certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.False(result);
    }

    [Fact]
    public void Create_AcceptsSelfSignedChain_WhenExplicitlyAllowed()
    {
        var options = new TrinoTlsOptions { AllowSelfSignedCertificate = true };
        var callback = CertificateValidator.Create(options, logger: null);
        using var certificate = CreateSelfSignedCertificate();
        using var chain = BuildChainFor(certificate);

        var result = callback(this, certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(result);
    }

    [Fact]
    public void CustomTrustedRoots_AreNeverAddedToClientCertificates()
    {
        using var root = CreateSelfSignedCertificate();
        var options = new TrinoTlsOptions();
        options.TrustedRootCertificates.Add(root);

        Assert.Empty(options.ClientCertificates);
    }

    [Fact]
    public void Create_RejectsCertificate_WhenPinningDoesNotMatch()
    {
        var options = new TrinoTlsOptions { CertificatePinning = new HashSet<string> { new string('0', 64) } };
        var callback = CertificateValidator.Create(options, logger: null);
        using var certificate = CreateSelfSignedCertificate();

        var result = callback(this, certificate, chain: null, SslPolicyErrors.None);

        Assert.False(result);
    }

    [Fact]
    public void Create_AcceptsCertificate_WhenPinningMatchesLeafSpki()
    {
        using var certificate = CreateSelfSignedCertificate();
        var spki = certificate.PublicKey.ExportSubjectPublicKeyInfo();
        var pin = Convert.ToHexString(SHA256.HashData(spki));

        var options = new TrinoTlsOptions { CertificatePinning = new HashSet<string> { pin } };
        var callback = CertificateValidator.Create(options, logger: null);

        var result = callback(this, certificate, chain: null, SslPolicyErrors.None);

        Assert.True(result);
    }

    [Fact]
    public void Create_BuildsChain_UsingCustomTrustedRoot()
    {
        // Both validity windows derive from one timestamp: re-reading UtcNow per certificate lets
        // the clock tick between them, giving the leaf a NotAfter past the root's, which
        // CertificateRequest.Create rejects outright.
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddDays(31);

        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=triql-test-root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(notBefore, notAfter);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=triql-test-leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var leaf = leafRequest.Create(root, notBefore, notAfter, Guid.NewGuid().ToByteArray());
        using var leafWithKey = leaf.CopyWithPrivateKey(leafKey);

        var options = new TrinoTlsOptions();
        options.TrustedRootCertificates.Add(root);
        var callback = CertificateValidator.Create(options, logger: null);

        var result = callback(this, leafWithKey, chain: null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(result);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=triql-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Chain BuildChainFor(X509Certificate2 certificate)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(certificate);
        return chain;
    }
}
