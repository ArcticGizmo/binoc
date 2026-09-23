using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Binoc.Core.Model;

namespace Binoc.Core.Signing;

/// <summary>
/// Reduces a DER-encoded X.509 certificate to the <see cref="CertInfo"/> the report shows, using only the
/// BCL (decision D1 — no OpenSSL, no BouncyCastle). Flags the Android debug keystore certificate and
/// whether the cert is self-signed.
/// </summary>
public static class CertSummary
{
    // The fixed subject of the auto-generated Android debug keystore key.
    private const string AndroidDebugSubject = "CN=Android Debug";

    public static CertInfo? From(byte[] der)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(der);

            string sha256 = Hex(cert.GetCertHash(HashAlgorithmName.SHA256));
            string sha1 = Hex(cert.GetCertHash(HashAlgorithmName.SHA1));
            bool selfSigned = string.Equals(cert.SubjectName.Name, cert.IssuerName.Name, StringComparison.Ordinal);
            bool debugKey = cert.SubjectName.Name.Contains(AndroidDebugSubject, StringComparison.OrdinalIgnoreCase);

            return new CertInfo(
                Subject: cert.SubjectName.Name,
                Issuer: cert.IssuerName.Name,
                Sha256Fingerprint: sha256,
                Sha1Fingerprint: sha1,
                NotBefore: cert.NotBefore.ToUniversalTime(),
                NotAfter: cert.NotAfter.ToUniversalTime(),
                SelfSigned: selfSigned,
                IsAndroidDebugKey: debugKey,
                SignatureAlgorithm: cert.SignatureAlgorithm.FriendlyName ?? cert.SignatureAlgorithm.Value ?? "unknown",
                KeySizeBits: KeySize(cert));
        }
        catch
        {
            return null;
        }
    }

    private static int KeySize(X509Certificate2 cert)
    {
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa is not null) return rsa.KeySize;
            using var ec = cert.GetECDsaPublicKey();
            if (ec is not null) return ec.KeySize;
            using var dsa = cert.GetDSAPublicKey();
            if (dsa is not null) return dsa.KeySize;
        }
        catch { }
        return 0;
    }

    // Upper-case colon-separated hex, the conventional fingerprint form.
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes) is var h
        ? string.Join(':', Enumerable.Range(0, h.Length / 2).Select(i => h.Substring(i * 2, 2)))
        : string.Empty;
}
