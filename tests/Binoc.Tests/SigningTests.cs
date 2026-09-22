using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Binoc.Core.Analysis;
using Binoc.Core.Android;
using Binoc.Core.Model;
using Binoc.Core.Signing;
using Xunit;

namespace Binoc.Tests;

public class SigningTests
{
    private static X509Certificate2 MakeCert(string subject)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    // ── CertSummary ──
    [Fact]
    public void CertSummary_reads_a_self_signed_cert()
    {
        using var cert = MakeCert("CN=Test Signer, O=binoc");
        var info = CertSummary.From(cert.Export(X509ContentType.Cert));

        Assert.NotNull(info);
        Assert.Contains("Test Signer", info!.Subject);
        Assert.True(info.SelfSigned);
        Assert.False(info.IsAndroidDebugKey);
        Assert.False(info.IsExpiredOrNotYetValid);
        Assert.Equal(2048, info.KeySizeBits);
        Assert.Matches("^([0-9A-F]{2}:){31}[0-9A-F]{2}$", info.Sha256Fingerprint);
    }

    [Fact]
    public void CertSummary_flags_the_android_debug_key()
    {
        using var cert = MakeCert("CN=Android Debug, O=Android, C=US");
        var info = CertSummary.From(cert.Export(X509ContentType.Cert));
        Assert.True(info!.IsAndroidDebugKey);
    }

    // ── APK Signing Block (v2) ──
    private static byte[] Len(byte[] payload)
    {
        var b = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)payload.Length);
        payload.CopyTo(b, 4);
        return b;
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }

    // Builds a file: [v2 signing block][EOCD], with EOCD.cdOffset pointing just past the block.
    private static byte[] BuildApkWithV2Block(byte[] certDer)
    {
        // value = signers→signer→signedData→(digests, certificates→cert)
        byte[] value = Len(Len(Len(Cat(
            Len(Array.Empty<byte>()),     // digests (empty)
            Len(Len(certDer))))));         // certificates → first cert

        // pair = uint64 pairLen | uint32 id | value
        var pair = new MemoryStream();
        var lenBuf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(lenBuf, (ulong)(4 + value.Length));
        pair.Write(lenBuf);
        var idBuf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(idBuf, 0x7109871a); // v2
        pair.Write(idBuf);
        pair.Write(value);
        var pairs = pair.ToArray();

        long blockSize = pairs.Length + 24; // pairs + trailing size(8) + magic(16)
        var block = new MemoryStream();
        var sz = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(sz, (ulong)blockSize); block.Write(sz);
        block.Write(pairs);
        block.Write(sz);
        block.Write("APK Sig Block 42"u8.ToArray());
        var blockBytes = block.ToArray();

        uint cdOffset = (uint)blockBytes.Length;

        var eocd = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(0), 0x06054b50);
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), cdOffset);
        return Cat(blockBytes, eocd);
    }

    [Fact]
    public void ApkSignatureReader_extracts_the_v2_signer_cert()
    {
        using var cert = MakeCert("CN=APK Signer, O=binoc");
        var certDer = cert.Export(X509ContentType.Cert);
        var path = Path.Combine(Path.GetTempPath(), $"binoc-sig-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(path, BuildApkWithV2Block(certDer));
        try
        {
            var result = ApkSignatureReader.Read(path);
            Assert.NotNull(result);
            Assert.True(result!.HasV2);
            Assert.NotNull(result.SignerCertDer);

            var info = CertSummary.From(result.SignerCertDer!);
            Assert.Contains("APK Signer", info!.Subject);
        }
        finally { File.Delete(path); }
    }

    // ── IPA signing via embedded.mobileprovision (CMS → plist → DeveloperCertificates) ──
    [Fact]
    public void Ipa_signing_reads_the_developer_certificate_from_the_provisioning_profile()
    {
        using var devCert = MakeCert("CN=Apple Distribution: Test Co, O=Test Co");
        var devDer = devCert.Export(X509ContentType.Cert);
        var b64 = Convert.ToBase64String(devDer);

        string plistXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>DeveloperCertificates</key>
              <array><data>{b64}</data></array>
            </dict></plist>
            """;

        // Wrap the plist in a CMS SignedData, as a real embedded.mobileprovision is.
        using var signer = MakeCert("CN=Apple, O=Apple Inc.");
        var cms = new SignedCms(new ContentInfo(System.Text.Encoding.UTF8.GetBytes(plistXml)));
        cms.ComputeSignature(new CmsSigner(signer));
        var provision = cms.Encode();

        var path = Path.Combine(Path.GetTempPath(), $"binoc-sig-{Guid.NewGuid():N}.ipa");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var i = zip.CreateEntry("Payload/Demo.app/Info.plist").Open())
                i.Write(System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><plist><dict/></plist>"));
            using (var m = zip.CreateEntry("Payload/Demo.app/embedded.mobileprovision").Open()) m.Write(provision);
            using (var c = zip.CreateEntry("Payload/Demo.app/_CodeSignature/CodeResources").Open()) c.Write(new byte[] { 0 });
        }
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Signing);
            Assert.Contains("Apple code signing", report.Signing!.Schemes);
            Assert.NotNull(report.Signing.Certificate);
            Assert.Contains("Apple Distribution: Test Co", report.Signing.Certificate!.Subject);
        }
        finally { File.Delete(path); }
    }

    // ── AAB upload-key (JAR / PKCS#7) end to end ──
    [Fact]
    public void Aab_signing_reads_upload_key_and_flags_it()
    {
        using var cert = MakeCert("CN=Upload Key, O=binoc");
        var cms = new SignedCms(new ContentInfo(new byte[] { 1, 2, 3 }));
        cms.ComputeSignature(new CmsSigner(cert));
        var pkcs7 = cms.Encode();

        var path = Path.Combine(Path.GetTempPath(), $"binoc-sig-{Guid.NewGuid():N}.aab");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var c = zip.CreateEntry("BundleConfig.pb").Open()) c.Write(new byte[] { 0 });
            using (var m = zip.CreateEntry("base/manifest/AndroidManifest.xml").Open()) m.Write(new byte[] { 0 });
            using (var s = zip.CreateEntry("META-INF/UPLOAD.SF").Open()) s.Write(new byte[] { 0 });
            using (var r = zip.CreateEntry("META-INF/UPLOAD.RSA").Open()) r.Write(pkcs7);
        }
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Signing);
            Assert.True(report.Signing!.UploadKeyNotDistribution);
            Assert.Contains("Upload Key", report.Signing.Certificate!.Subject);
            Assert.Contains(report.Notes, n => n.Category == "signing" && n.Message.Contains("upload key"));
        }
        finally { File.Delete(path); }
    }
}
