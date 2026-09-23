using System.IO.Compression;
using Binoc.Core.Analysis;
using Binoc.Core.Model;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// The archive walk's per-type bucketing, exercised through the pipeline against in-memory archives so the
/// classification rules are asserted end to end.
/// </summary>
public class EntryClassifierTests
{
    private static string WriteZip(string ext, params (string name, int bytes)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-cls-{Guid.NewGuid():N}{ext}");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var e = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(new byte[bytes]);
        }
        return path;
    }

    [Fact]
    public void Apk_entries_group_into_expected_buckets()
    {
        var path = WriteZip(".apk",
            ("AndroidManifest.xml", 10),
            ("classes.dex", 100),
            ("classes2.dex", 50),
            ("lib/arm64-v8a/libfoo.so", 200),
            ("res/layout/main.xml", 20),
            ("resources.arsc", 30),
            ("assets/data.json", 15),
            ("META-INF/CERT.RSA", 5));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            var labels = report.Archive!.Buckets.Select(b => b.Label).ToHashSet();

            Assert.Contains("DEX code", labels);
            Assert.Contains("Native libraries", labels);
            Assert.Contains("Resources", labels);
            Assert.Contains("Assets", labels);
            Assert.Contains("Signatures & metadata", labels);

            // Two dex entries collapse into one bucket with the combined count.
            var dex = report.Archive.Buckets.Single(b => b.Label == "DEX code");
            Assert.Equal(2, dex.EntryCount);

            // Largest compressed bucket first — native libs (200 bytes) here.
            Assert.Equal("Native libraries", report.Archive.Buckets[0].Label);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ipa_entries_group_into_ios_buckets()
    {
        var path = WriteZip(".ipa",
            ("Payload/My.app/Info.plist", 10),
            ("Payload/My.app/My", 500),
            ("Payload/My.app/Frameworks/Lib.framework/Lib", 300),
            ("Payload/My.app/PlugIns/Ext.appex/Ext", 120),
            ("Payload/My.app/_CodeSignature/CodeResources", 40),
            ("Payload/My.app/Assets.car", 80));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            var labels = report.Archive!.Buckets.Select(b => b.Label).ToHashSet();

            Assert.Contains("Frameworks", labels);
            Assert.Contains("App extensions", labels);
            Assert.Contains("Code signature", labels);
            Assert.Contains("Asset catalogs", labels);
            Assert.Contains("Resources", labels);   // Info.plist
        }
        finally { File.Delete(path); }
    }
}
