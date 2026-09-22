using System.IO.Compression;
using Binoc.Core.Analysis;
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
