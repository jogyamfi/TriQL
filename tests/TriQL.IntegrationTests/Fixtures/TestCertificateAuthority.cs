using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TriQL.IntegrationTests.Fixtures;

/// <summary>
/// Test-only self-signed PKI used to give both MinIO and the spooling-configured Trino coordinator
/// real HTTPS listeners for Phase 7's Lane B tests (P7-T1/P7-T2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> <c>UriGuard</c> (<c>src/TriQL.Client/Internal/UriGuard.cs</c>)
/// requires <c>https</c> unconditionally for both a spooled segment's <c>uri</c> and its
/// <c>ackUri</c>, regardless of the coordinator's own scheme — see
/// <c>UriGuardTests.ValidateSegmentUri_HttpDowngrade_RejectedEvenOffOrigin</c> and
/// <c>ValidateAckUri_HttpDowngrade_Rejected</c>. A plain-HTTP MinIO endpoint (the setup the Phase 7
/// task brief flagged as "likely simplest") is therefore rejected outright the moment a real segment
/// fetch is attempted — not a hypothetical, this was verified against this exact client code. So
/// both MinIO and the Trino coordinator need real TLS listeners, which this class provides via one
/// self-signed CA that signs a leaf certificate for each service.
/// </para>
/// <para>
/// The TriQL client's own TLS validation is satisfied without trusting the CA at all: the real
/// tests set <c>TrinoTlsOptions.AllowSelfSignedCertificate = true</c> and
/// <c>AllowHostNameMismatch = true</c>, which <c>CertificateValidator</c> honours for every
/// connection made through the client's single shared <c>SocketsHttpHandler</c> — the coordinator,
/// the segment fetch, and the ack fetch alike (confirmed via <c>HttpHandlerFactory.Create</c>, which
/// builds exactly one handler for all three). The CA is still generated and exported because two
/// other parties need to trust it explicitly: MinIO's own health-check probe performed by this test
/// fixture, and — critically — Trino's own outbound S3 client, which must trust MinIO's certificate
/// to spool segments there at all (see <see cref="Pkcs12TrustStore"/>).
/// </para>
/// </remarks>
public sealed class TestCertificateAuthority : IDisposable
{
    private const string TrustStorePassword = "triql-test-truststore";
    private const string KeystorePassword = "triql-test-keystore";

    private readonly X509Certificate2 _caWithKey;

    private TestCertificateAuthority(X509Certificate2 caWithKey)
    {
        _caWithKey = caWithKey;
    }

    public static TestCertificateAuthority Create()
    {
        using var caKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=TriQL Phase 7 Test CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddDays(2);
        var ca = request.CreateSelfSigned(notBefore, notAfter);

        // CreateSelfSigned's returned certificate does not reliably retain an exportable private
        // key handle across all platforms; re-import from PFX to get a stable, exportable instance.
        var exported = ca.Export(X509ContentType.Pfx, TrustStorePassword);
        ca.Dispose();
#if NET9_0_OR_GREATER
        var reimported = X509CertificateLoader.LoadPkcs12(exported, TrustStorePassword, X509KeyStorageFlags.Exportable);
#else
        var reimported = new X509Certificate2(exported, TrustStorePassword, X509KeyStorageFlags.Exportable);
#endif
        return new TestCertificateAuthority(reimported);
    }

    /// <summary>
    /// Issues a server (<c>serverAuth</c>) leaf certificate signed by this CA, with the given SAN
    /// entries, and returns it with an attached exportable private key.
    /// </summary>
    public X509Certificate2 IssueServerCertificate(string commonName, IReadOnlyList<string> dnsNames, IReadOnlyList<IPAddress> ipAddresses)
    {
        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")], false)); // serverAuth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var dns in dnsNames)
        {
            sanBuilder.AddDnsName(dns);
        }

        foreach (var ip in ipAddresses)
        {
            sanBuilder.AddIpAddress(ip);
        }

        request.CertificateExtensions.Add(sanBuilder.Build());

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        // Must not exceed the CA's own NotAfter, or CertificateRequest.Create throws. The CA was
        // minted earlier (at fixture-construction time); a leaf issued "now" plus a fixed offset
        // can therefore land slightly past the CA's own expiry.
        var notAfter = DateTimeOffset.UtcNow.AddDays(2);
        if (notAfter > _caWithKey.NotAfter)
        {
            notAfter = _caWithKey.NotAfter;
        }

        using var leafPublicOnly = request.Create(_caWithKey, notBefore, notAfter, serial);
        var leafWithKey = leafPublicOnly.CopyWithPrivateKey(leafKey);

        // Same re-import dance as the CA: ensures the private key survives export round-trips
        // (needed for both the PEM export below and the PKCS12 keystore export).
        var exported = leafWithKey.Export(X509ContentType.Pfx, KeystorePassword);
        leafWithKey.Dispose();
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(exported, KeystorePassword, X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(exported, KeystorePassword, X509KeyStorageFlags.Exportable);
#endif
    }

    /// <summary>PEM-encodes a leaf certificate's public certificate (for MinIO's <c>certs/public.crt</c>).</summary>
    public static string ToCertificatePem(X509Certificate2 certificate) => certificate.ExportCertificatePem();

    /// <summary>PEM-encodes a leaf certificate's RSA private key (for MinIO's <c>certs/private.key</c>).</summary>
    public static string ToPrivateKeyPem(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("Certificate has no RSA private key to export.");
        return rsa.ExportRSAPrivateKeyPem();
    }

    /// <summary>A PKCS12 keystore containing <paramref name="certificate"/> and its private key, for Trino's <c>http-server.https.keystore.path</c>.</summary>
    public static byte[] ToPkcs12Keystore(X509Certificate2 certificate) => certificate.Export(X509ContentType.Pfx, KeystorePassword);

    /// <summary>The password used for every PKCS12 keystore this class exports.</summary>
    public static string KeystorePasswordValue => KeystorePassword;

    /// <summary>
    /// A PKCS12 truststore for Trino's outbound S3 client to trust MinIO's certificate via
    /// <c>-Djavax.net.ssl.trustStore</c>/<c>-Djavax.net.ssl.trustStoreType=PKCS12</c>.
    /// </summary>
    /// <remarks>
    /// Exports the CA <i>with</i> its private key (a "key entry", not a cert-only "trusted
    /// certificate entry"), even though only the public certificate is strictly needed for trust
    /// purposes. A cert-only PKCS12 collection built via <c>X509Certificate2Collection.Export</c>
    /// was tried first and round-tripped fine through .NET's own certificate APIs, but the target
    /// JVM's <c>TrustManagerFactory</c> rejected it with
    /// <c>InvalidAlgorithmParameterException: the trustAnchors parameter must be non-empty</c> —
    /// i.e. it parsed as containing zero usable certificates. A key-entry PKCS12 (the same export
    /// path already proven to work for the HTTPS keystore below) does not have that problem: a JVM
    /// keystore's certificate chain in a key entry counts as a trust anchor once loaded into a
    /// <c>TrustManagerFactory</c> just as a dedicated trusted-certificate entry would.
    /// </remarks>
    public byte[] Pkcs12TrustStore() => _caWithKey.Export(X509ContentType.Pfx, TrustStorePassword);

    /// <summary>The password used for <see cref="Pkcs12TrustStore"/>.</summary>
    public static string TrustStorePasswordValue => TrustStorePassword;

    /// <summary>The CA's own public certificate (no private key), for building a trusting <see cref="HttpClientHandler"/>.</summary>
    public X509Certificate2 PublicCertificate => X509CertificateLoaderExtensions_CertificateOnly(_caWithKey);

    /// <summary>
    /// Builds an <see cref="HttpMessageHandler"/> whose only accepted trust basis is this CA —
    /// used by this test project's own readiness probes and raw-MinIO verification calls, never by
    /// the TriQL client under test (which uses its own <c>TrinoTlsOptions</c> instead).
    /// </summary>
    public HttpClientHandler CreateTrustingHandler()
    {
        var caThumbprint = _caWithKey.Thumbprint;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null)
                {
                    return false;
                }

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Clear();
                chain.ChainPolicy.CustomTrustStore.Add(_caWithKey);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                using var leaf = new X509Certificate2(cert);
                return chain.Build(leaf) && ChainRootMatches(chain, caThumbprint);
            },
        };
        return handler;
    }

    private static bool ChainRootMatches(X509Chain chain, string caThumbprint)
    {
        foreach (var element in chain.ChainElements)
        {
            if (string.Equals(element.Certificate.Thumbprint, caThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static X509Certificate2 X509CertificateLoaderExtensions_CertificateOnly(X509Certificate2 withKey)
    {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadCertificate(withKey.Export(X509ContentType.Cert));
#else
        return new X509Certificate2(withKey.Export(X509ContentType.Cert));
#endif
    }

    public void Dispose() => _caWithKey.Dispose();
}
