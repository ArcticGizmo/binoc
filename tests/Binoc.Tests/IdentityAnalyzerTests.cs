using System.IO.Compression;
using System.Text;
using Binoc.Core.Analysis;
using Binoc.Core.Model;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// Identity extraction end to end: an APK-shaped zip carrying a real AXML manifest (built by
/// <see cref="AxmlTestBuilder"/>) run through the pipeline.
/// </summary>
public class IdentityAnalyzerTests
{
    private static string WriteApk(byte[] manifest)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-id-{Guid.NewGuid():N}.apk");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var m = zip.CreateEntry("AndroidManifest.xml");
        using (var s = m.Open()) s.Write(manifest);
        using (var d = zip.CreateEntry("classes.dex").Open()) d.Write(new byte[] { 1, 2, 3 });
        return path;
    }

    private static byte[] SampleManifest(bool debuggable)
    {
        var strings = new List<string>
        {
            "manifest", "uses-sdk", "application",
            "package", "versionCode", "versionName", "minSdkVersion", "targetSdkVersion", "debuggable",
            "com.example.app", "1.2.3",
        };
        var b = new AxmlTestBuilder(strings);
        var manifest = b.StartElement(0, new[]
        {
            AxmlTestBuilder.StringAttr(3, 9),
            AxmlTestBuilder.IntAttr(4, 42),
            AxmlTestBuilder.StringAttr(5, 10),
        });
        var usesSdk = b.StartElement(1, new[]
        {
            AxmlTestBuilder.IntAttr(6, 24),
            AxmlTestBuilder.IntAttr(7, 34),
        });
        var application = b.StartElement(2, new[] { AxmlTestBuilder.BoolAttr(8, debuggable) });
        return AxmlTestBuilder.File(b.BuildStringPool(), manifest, usesSdk, application);
    }

    [Fact]
    public void Apk_identity_is_extracted_from_the_manifest()
    {
        var path = WriteApk(SampleManifest(debuggable: false));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Identity);
            Assert.Equal("com.example.app", report.Identity!.PackageId);
            Assert.Equal("1.2.3", report.Identity.VersionName);
            Assert.Equal("42", report.Identity.VersionCode);
            Assert.Equal("24", report.Identity.MinSdk);
            Assert.Equal("34", report.Identity.TargetSdk);
            Assert.Equal(false, report.Identity.IsDebuggable);
        }
        finally { File.Delete(path); }
    }

    private static string WriteIpa(string infoPlistXml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-id-{Guid.NewGuid():N}.ipa");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("Payload/Demo.app/Info.plist").Open()) s.Write(Encoding.UTF8.GetBytes(infoPlistXml));
        using (var e = zip.CreateEntry("Payload/Demo.app/Demo").Open()) e.Write(new byte[] { 0xCF, 0xFA, 0xED, 0xFE });
        return path;
    }

    [Fact]
    public void Ipa_identity_is_extracted_from_info_plist()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>com.example.ios</string>
              <key>CFBundleShortVersionString</key><string>3.4</string>
              <key>CFBundleVersion</key><string>3401</string>
              <key>MinimumOSVersion</key><string>15.0</string>
              <key>UIDeviceFamily</key><array><integer>1</integer><integer>2</integer></array>
              <key>DTSDKName</key><string>iphoneos17.0</string>
              <key>DTXcode</key><string>1500</string>
            </dict></plist>
            """;
        var path = WriteIpa(xml);
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Identity);
            Assert.Equal("com.example.ios", report.Identity!.PackageId);
            Assert.Equal("3.4", report.Identity.VersionName);
            Assert.Equal("3401", report.Identity.VersionCode);
            Assert.Equal("15.0", report.Identity.MinimumOsVersion);
            Assert.Equal("iPhone/iPod, iPad", report.Identity.DeviceFamily);
            Assert.Contains("iphoneos17.0", report.Identity.BuildProvenance);
        }
        finally { File.Delete(path); }
    }

    private static string WriteAab(byte[] protoManifest)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-id-{Guid.NewGuid():N}.aab");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("base/manifest/AndroidManifest.xml").Open()) s.Write(protoManifest);
        using (var c = zip.CreateEntry("BundleConfig.pb").Open()) c.Write(new byte[] { 0 });
        using (var d = zip.CreateEntry("base/dex/classes.dex").Open()) d.Write(new byte[] { 1 });
        return path;
    }

    [Fact]
    public void Aab_identity_is_extracted_from_the_protobuf_manifest()
    {
        var path = WriteAab(PbManifestReaderTests.Sample(debuggable: false));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.Equal(BinaryFormat.Aab, report.Format);
            Assert.NotNull(report.Identity);
            Assert.Equal("com.example.bundle", report.Identity!.PackageId);
            Assert.Equal("88", report.Identity.VersionCode);
            Assert.Equal("9.9", report.Identity.VersionName);
            Assert.Equal("24", report.Identity.MinSdk);
            Assert.Equal("34", report.Identity.TargetSdk);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Debuggable_apk_raises_a_warning_note()
    {
        var path = WriteApk(SampleManifest(debuggable: true));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.Equal(true, report.Identity!.IsDebuggable);
            Assert.Contains(report.Notes, n =>
                n.Category == "identity" && n.Message.Contains("debuggable", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }
}
