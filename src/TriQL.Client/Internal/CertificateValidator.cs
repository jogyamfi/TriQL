using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace TriQL.Client.Internal;

/// <summary>
/// Builds the certificate validation callback for a <see cref="TrinoTlsOptions"/> instance.
/// Rejects any <see cref="SslPolicyErrors"/> value not explicitly permitted (FR-3.2.2); never a blanket accept.
/// </summary>
internal static class CertificateValidator
{
    public static RemoteCertificateValidationCallback Create(TrinoTlsOptions options, ILogger? logger)
    {
        if (logger is not null && (options.AllowSelfSignedCertificate || options.AllowHostNameMismatch))
        {
            Log.WeakenedTlsValidation(logger, options.AllowSelfSignedCertificate, options.AllowHostNameMismatch);
        }

        var customRoots = LoadCustomRoots(options);

        return (_, certificate, chain, sslPolicyErrors) =>
            Validate(options, customRoots, certificate, chain, sslPolicyErrors);
    }

    private static bool Validate(
        TrinoTlsOptions options,
        X509Certificate2Collection customRoots,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
        {
            return false;
        }

        using var leaf = new X509Certificate2(certificate);

        if (options.CertificatePinning is { Count: > 0 } pins && !MatchesPin(leaf, pins))
        {
            return false;
        }

        var remainingErrors = sslPolicyErrors;

        if (customRoots.Count > 0 && (!options.UseSystemTrustStore || remainingErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)))
        {
            // A custom trust store overrides the OS-computed chain result entirely: either it
            // resolves the chain error, or (when the system store is disabled) it is the only
            // basis for trust even if the OS store had already accepted the chain.
            if (ValidateWithCustomRoots(leaf, customRoots))
            {
                remainingErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
            }
            else if (!options.UseSystemTrustStore)
            {
                remainingErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
            }
        }
        else if (remainingErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)
            && options.AllowSelfSignedCertificate
            && IsOnlyUntrustedRoot(chain))
        {
            remainingErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
        }

        if (options.AllowHostNameMismatch)
        {
            remainingErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
        }

        return remainingErrors == SslPolicyErrors.None;
    }

    private static bool ValidateWithCustomRoots(X509Certificate2 leaf, X509Certificate2Collection customRoots)
    {
        // FR-3.2.4: custom roots are validated via CustomTrustStore/CustomRootTrust, never by
        // adding them to the handler's ClientCertificates.
        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Clear();
        customChain.ChainPolicy.CustomTrustStore.AddRange(customRoots);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return customChain.Build(leaf);
    }

    private static bool IsOnlyUntrustedRoot(X509Chain? chain)
    {
        if (chain is null || chain.ChainStatus.Length == 0)
        {
            return false;
        }

        return chain.ChainStatus.All(status =>
            status.Status is X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain or X509ChainStatusFlags.NoError);
    }

    private static bool MatchesPin(X509Certificate2 leaf, ISet<string> pins)
    {
        var spki = leaf.PublicKey.ExportSubjectPublicKeyInfo();
        var hash = Convert.ToHexString(SHA256.HashData(spki));
        return pins.Any(pin => string.Equals(pin, hash, StringComparison.OrdinalIgnoreCase));
    }

    private static X509Certificate2Collection LoadCustomRoots(TrinoTlsOptions options)
    {
        var roots = new X509Certificate2Collection();
        foreach (var root in options.TrustedRootCertificates)
        {
            roots.Add(root);
        }

        if (!string.IsNullOrEmpty(options.TrustedRootCertificatePath))
        {
            roots.Add(LoadRootFromFile(options.TrustedRootCertificatePath));
        }

        return roots;
    }

    private static X509Certificate2 LoadRootFromFile(string path) =>
#if NET9_0_OR_GREATER
        X509CertificateLoader.LoadCertificateFromFile(path);
#else
        new X509Certificate2(path);
#endif
}
