using System.IO.Compression;
using System.Security.Cryptography.Pkcs;

namespace Binoc.Core.Signing;

/// <summary>
/// Reads legacy JAR (v1) signing from an archive's <c>META-INF</c>: the presence of a signature file
/// (<c>*.SF</c>) plus a PKCS#7 signature block (<c>*.RSA</c>/<c>*.DSA</c>/<c>*.EC</c>), and the signer
/// certificate out of that block via the BCL's <see cref="SignedCms"/> (decision D1). This is how APKs are
/// v1-signed and how an AAB is signed with its upload key.
/// </summary>
public static class JarSignatureReader
{
    public sealed record Result(bool HasV1, byte[]? SignerCertDer);

    public static Result Read(ZipArchive archive)
    {
        var metaInf = archive.Entries
            .Where(e => e.FullName.Replace('\\', '/').StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool hasSf = metaInf.Any(e => e.FullName.EndsWith(".SF", StringComparison.OrdinalIgnoreCase));
        var block = metaInf.FirstOrDefault(e =>
            e.FullName.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".EC", StringComparison.OrdinalIgnoreCase));

        if (block is null) return new Result(false, null);

        byte[]? certDer = null;
        try
        {
            using var s = block.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);

            var cms = new SignedCms();
            cms.Decode(ms.ToArray());
            var signer = cms.SignerInfos.Count > 0 ? cms.SignerInfos[0].Certificate : null;
            certDer = signer?.RawData ?? (cms.Certificates.Count > 0 ? cms.Certificates[0].RawData : null);
        }
        catch { /* malformed block — still report v1 present */ }

        return new Result(hasSf, certDer);
    }
}
