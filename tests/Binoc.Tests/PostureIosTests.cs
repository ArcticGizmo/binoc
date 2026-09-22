using System.IO.Compression;
using System.Text;
using Binoc.Core.Analysis;
using Xunit;

namespace Binoc.Tests;

public class PostureIosTests
{
    private static string WriteIpa(string infoPlistXml, bool withPrivacyManifest)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-posture-{Guid.NewGuid():N}.ipa");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var i = zip.CreateEntry("Payload/Demo.app/Info.plist").Open()) i.Write(Encoding.UTF8.GetBytes(infoPlistXml));
        if (withPrivacyManifest)
            using (var p = zip.CreateEntry("Payload/Demo.app/PrivacyInfo.xcprivacy").Open()) p.Write(new byte[] { 0 });
        return path;
    }

    [Fact]
    public void Reports_usage_descriptions_ats_url_schemes_and_missing_privacy_manifest()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>NSCameraUsageDescription</key><string>Scan documents</string>
              <key>NSLocationWhenInUseUsageDescription</key><string>Show nearby help</string>
              <key>NSAppTransportSecurity</key><dict>
                <key>NSAllowsArbitraryLoads</key><true/>
                <key>NSExceptionDomains</key><dict>
                  <key>legacy.example.com</key><dict><key>NSExceptionAllowsInsecureHTTPLoads</key><true/></dict>
                </dict>
              </dict>
              <key>CFBundleURLTypes</key><array><dict>
                <key>CFBundleURLSchemes</key><array><string>myapp</string><string>myapp-alt</string></array>
              </dict></array>
            </dict></plist>
            """;
        var path = WriteIpa(xml, withPrivacyManifest: false);
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.SecurityPosture);
            var p = report.SecurityPosture!;
            Assert.Equal(2, p.UsageDescriptions.Count);
            Assert.Contains(p.UsageDescriptions, u => u.Key == "NSCameraUsageDescription" && u.Purpose == "Scan documents");
            Assert.Equal(true, p.AtsAllowsArbitraryLoads);
            Assert.Contains("legacy.example.com", p.AtsExceptionDomains);
            Assert.Equal(new[] { "myapp", "myapp-alt" }, p.UrlSchemes);
            Assert.Equal(false, p.HasPrivacyManifest);
            Assert.Contains(report.Notes, n => n.Category == "posture" && n.Message.Contains("Arbitrary", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Detects_present_privacy_manifest()
    {
        const string xml = "<?xml version=\"1.0\"?><plist><dict/></plist>";
        var path = WriteIpa(xml, withPrivacyManifest: true);
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            Assert.Equal(true, report.SecurityPosture!.HasPrivacyManifest);
        }
        finally { File.Delete(path); }
    }
}
