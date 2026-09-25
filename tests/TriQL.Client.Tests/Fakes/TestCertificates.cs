using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TriQL.Client.Tests.Fakes;

/// <summary>
/// Deterministically valid test certificates. <see cref="CertificateRequest.CreateSelfSigned(DateTimeOffset, DateTimeOffset)"/>
/// picks a random serial internally and can intermittently (~1 in 256) produce one with a redundant
/// leading byte, which the DER encoder rejects with "The first 9 bits of the integer value all have
/// the same value". Raw random bytes (including <c>Guid.ToByteArray()</c>) have the same flaw, so
/// every serial here goes through <see cref="NewSerialNumber"/>.
/// </summary>
internal static class TestCertificates
{
    /// <summary>A random 16-byte serial that is always positive and minimally encoded.</summary>
    public static byte[] NewSerialNumber()
    {
        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        // High bit clear keeps it positive; bit 6 set keeps the leading byte non-redundant.
        serial[0] = (byte)((serial[0] & 0x7F) | 0x40);
        return serial;
    }

    /// <summary>Equivalent to <c>CreateSelfSigned</c> for an RSA/PKCS#1 request, but with a safe serial.</summary>
    public static X509Certificate2 CreateSelfSigned(CertificateRequest request, RSA key, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var generator = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        using var publicOnly = request.Create(request.SubjectName, generator, notBefore, notAfter, NewSerialNumber());
        return publicOnly.CopyWithPrivateKey(key);
    }
}
