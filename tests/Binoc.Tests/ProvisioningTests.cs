using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Binoc.Core.Analysis;
using Xunit;

namespace Binoc.Tests;

public class ProvisioningTests
{
    // Wraps a plist in a CMS SignedData, like a real embedded.mobileprovision, and packs it into an IPA.
    private static string WriteIpaWithProfile(string plistXml)
    {
        using var rsa = RSA.Create(2048);
        var signer = new CertificateRequest("CN=Apple", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(plistXml)));
        cms.ComputeSignature(new CmsSigner(signer));
        var provision = cms.Encode();

        var path = Path.Combine(Path.GetTempPath(), $"binoc-prov-{Guid.NewGuid():N}.ipa");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var i = zip.CreateEntry("Payload/Demo.app/Info.plist").Open())
            i.Write(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><plist><dict/></plist>"));
        using (var p = zip.CreateEntry("Payload/Demo.app/embedded.mobileprovision").Open()) p.Write(provision);
        return path;
    }

    [Fact]
    public void Development_profile_is_inferred_from_devices_and_get_task_allow()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>Name</key><string>Demo Dev Profile</string>
              <key>TeamName</key><string>Test Co</string>
              <key>TeamIdentifier</key><array><string>ABCDE12345</string></array>
              <key>ProvisionedDevices</key><array><string>udid-1</string><string>udid-2</string></array>
              <key>ExpirationDate</key><date>2030-01-01T00:00:00Z</date>
              <key>Entitlements</key><dict>
                <key>application-identifier</key><string>ABCDE12345.com.example</string>
                <key>get-task-allow</key><true/>
                <key>aps-environment</key><string>development</string>
              </dict>
            </dict></plist>
            """;
        var path = WriteIpaWithProfile(xml);
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Provisioning);
            var p = report.Provisioning!;
            Assert.Equal("Development", p.DistributionType);
            Assert.Equal("Demo Dev Profile", p.Name);
            Assert.Equal("Test Co", p.TeamName);
            Assert.Equal("ABCDE12345", p.TeamId);
            Assert.Equal("ABCDE12345.com.example", p.AppId);
            Assert.Equal(true, p.GetTaskAllow);
            Assert.Equal(2, p.ProvisionedDeviceCount);
            Assert.Contains("aps-environment", p.Entitlements);
            Assert.Contains(report.Notes, n => n.Category == "provisioning" && n.Message.Contains("get-task-allow"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void App_store_profile_has_no_devices_and_no_debugging()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>Name</key><string>Demo Store</string>
              <key>Entitlements</key><dict>
                <key>get-task-allow</key><false/>
              </dict>
            </dict></plist>
            """;
        var path = WriteIpaWithProfile(xml);
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            Assert.Equal("App Store", report.Provisioning!.DistributionType);
            Assert.Equal(0, report.Provisioning.ProvisionedDeviceCount);
        }
        finally { File.Delete(path); }
    }
}
