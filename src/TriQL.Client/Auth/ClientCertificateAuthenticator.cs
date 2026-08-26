using System.Security.Cryptography.X509Certificates;

namespace TriQL.Client.Auth;

/// <summary>
/// Attaches one or more client certificates to the HTTP handler for mutual TLS. See FR-2.2.5.
/// </summary>
public sealed class ClientCertificateAuthenticator : ITrinoAuthenticator, IDisposable
{
    private readonly List<X509Certificate2> _certificates;

    private ClientCertificateAuthenticator(IEnumerable<X509Certificate2> certificates)
    {
        _certificates = [.. certificates];
    }

    /// <summary>Loads a certificate (and optional private key) from a PFX/PKCS#12 file.</summary>
    public static ClientCertificateAuthenticator FromFile(string path, string? password = null)
    {
#pragma warning disable CA2000 // Ownership transfers to the authenticator, which disposes it in Dispose().
        return new ClientCertificateAuthenticator([LoadFromFile(path, password)]);
#pragma warning restore CA2000
    }

    /// <summary>Loads a certificate and private key from PEM-encoded strings.</summary>
    public static ClientCertificateAuthenticator FromPem(string certificatePem, string privateKeyPem)
    {
#pragma warning disable CA2000 // Ownership transfers to the authenticator, which disposes it in Dispose().
        return new ClientCertificateAuthenticator([X509Certificate2.CreateFromPem(certificatePem, privateKeyPem)]);
#pragma warning restore CA2000
    }

    /// <summary>Loads a certificate (and optional private key) from raw PFX/PKCS#12 bytes.</summary>
    public static ClientCertificateAuthenticator FromBytes(byte[] rawData, string? password = null)
    {
#pragma warning disable CA2000 // Ownership transfers to the authenticator, which disposes it in Dispose().
        return new ClientCertificateAuthenticator([LoadFromBytes(rawData, password)]);
#pragma warning restore CA2000
    }

    /// <summary>Looks up a certificate by thumbprint in an <see cref="X509Store"/>.</summary>
    public static ClientCertificateAuthenticator FromStoreThumbprint(
        string thumbprint,
        StoreName storeName = StoreName.My,
        StoreLocation storeLocation = StoreLocation.CurrentUser)
    {
        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        if (matches.Count == 0)
        {
            throw new ArgumentException($"No certificate with thumbprint '{thumbprint}' was found in {storeLocation}/{storeName}.", nameof(thumbprint));
        }

        return new ClientCertificateAuthenticator([matches[0]]);
    }

    /// <inheritdoc/>
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask<bool> TryRefreshAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    /// <inheritdoc/>
    public void ConfigureHandler(SocketsHttpHandler handler)
    {
        handler.SslOptions.ClientCertificates ??= [];
        foreach (var certificate in _certificates)
        {
            handler.SslOptions.ClientCertificates.Add(certificate);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var certificate in _certificates)
        {
            certificate.Dispose();
        }
    }

    private static X509Certificate2 LoadFromFile(string path, string? password) =>
#if NET9_0_OR_GREATER
        password is null ? X509CertificateLoader.LoadCertificateFromFile(path) : X509CertificateLoader.LoadPkcs12FromFile(path, password);
#else
        password is null ? new X509Certificate2(path) : new X509Certificate2(path, password);
#endif

    private static X509Certificate2 LoadFromBytes(byte[] rawData, string? password) =>
#if NET9_0_OR_GREATER
        password is null ? X509CertificateLoader.LoadCertificate(rawData) : X509CertificateLoader.LoadPkcs12(rawData, password);
#else
        password is null ? new X509Certificate2(rawData) : new X509Certificate2(rawData, password);
#endif
}
