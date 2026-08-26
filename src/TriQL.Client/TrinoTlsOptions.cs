using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TriQL.Client;

/// <summary>
/// TLS policy for connections to the coordinator. See FR-3.2.1.
/// </summary>
public sealed class TrinoTlsOptions
{
    /// <summary>
    /// Accept a certificate chain whose only failure is an untrusted (self-signed) root.
    /// Scoped to that failure alone; other chain errors are never accepted. Default <see langword="false"/>.
    /// </summary>
    public bool AllowSelfSignedCertificate { get; set; }

    /// <summary>
    /// Accept a certificate whose only failure is a host name mismatch. Default <see langword="false"/>.
    /// </summary>
    public bool AllowHostNameMismatch { get; set; }

    /// <summary>
    /// Validate the server certificate against the operating system trust store. Default <see langword="true"/>.
    /// </summary>
    public bool UseSystemTrustStore { get; set; } = true;

    /// <summary>
    /// Additional trusted roots used to build a custom chain via <see cref="X509ChainTrustMode.CustomRootTrust"/>.
    /// These are never added to <see cref="ClientCertificates"/> (FR-3.2.4).
    /// </summary>
    public ICollection<X509Certificate2> TrustedRootCertificates { get; } = new List<X509Certificate2>();

    /// <summary>A PEM or DER file containing an additional trusted root certificate to load.</summary>
    public string? TrustedRootCertificatePath { get; set; }

    /// <summary>Client certificates presented for mutual TLS.</summary>
    public ICollection<X509Certificate2> ClientCertificates { get; } = new List<X509Certificate2>();

    /// <summary>
    /// An optional set of accepted leaf certificate SPKI SHA-256 pins (hex-encoded, case-insensitive).
    /// When set, the leaf certificate MUST match one of these pins in addition to passing chain validation.
    /// </summary>
    public ISet<string>? CertificatePinning { get; set; }

    /// <summary>
    /// Permit transmission of a bearer credential over an unencrypted <c>http</c> connection. Default <see langword="false"/>.
    /// See FR-1.1.3, SEC-3.
    /// </summary>
    public bool AllowPlaintextCredentials { get; set; }

    /// <summary>The minimum TLS protocol version enforced on the handler. Default TLS 1.2.</summary>
    public SslProtocols MinimumTlsVersion { get; set; } = SslProtocols.Tls12;
}
