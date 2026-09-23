using System.Security.Cryptography.Pkcs;

namespace Binoc.Core.Ios;

/// <summary>
/// Decodes an iOS <c>embedded.mobileprovision</c>: a CMS/PKCS#7 SignedData wrapping an XML plist. The BCL's
/// <see cref="SignedCms"/> unwraps the CMS (decision D1); the inner plist carries the signing identity in
/// <c>DeveloperCertificates</c> (DER), plus the provisioning details M3 will use (type, entitlements,
/// team, provisioned devices).
/// </summary>
public static class MobileProvisionReader
{
    public sealed record Result(IReadOnlyDictionary<string, object?> Plist, byte[]? SignerCertDer);

    public static Result? Read(byte[] cmsBytes)
    {
        try
        {
            var cms = new SignedCms();
            cms.Decode(cmsBytes);
            var content = cms.ContentInfo.Content;
            if (content is null || content.Length == 0) return null;

            var plist = PlistReader.ParseDict(content);

            byte[]? cert = null;
            if (plist.TryGetValue("DeveloperCertificates", out var v) && v is List<object?> certs)
                cert = certs.OfType<byte[]>().FirstOrDefault();

            return new Result(plist, cert);
        }
        catch
        {
            return null;
        }
    }
}
