using System.IO.Compression;
using System.Text;
using Binoc.Core.Analysis;
using Binoc.Core.Model;
using Xunit;

namespace Binoc.Tests;

public class ObfuscationAnalyzerTests
{
    private static string WriteApk(params (string name, byte[] data)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-obf-{Guid.NewGuid():N}.apk");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var m = zip.CreateEntry("AndroidManifest.xml").Open()) m.Write(new byte[] { 0 });
        using (var d = zip.CreateEntry("classes.dex").Open()) d.Write(new byte[] { 1 });
        foreach (var (name, data) in entries)
            using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        return path;
    }

    private static string WriteAab(params (string name, byte[] data)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-obf-{Guid.NewGuid():N}.aab");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var b = zip.CreateEntry("BundleConfig.pb").Open()) b.Write(new byte[] { 0 });     // detects as AAB
        using (var m = zip.CreateEntry("base/manifest/AndroidManifest.xml").Open()) m.Write(new byte[] { 0 });
        using (var d = zip.CreateEntry("base/dex/classes.dex").Open()) d.Write(new byte[] { 1 });
        foreach (var (name, data) in entries)
            using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        return path;
    }

    [Fact]
    public void Detects_commercial_packer()
    {
        var path = WriteApk(("lib/arm64-v8a/libjiagu.so", new byte[] { 1, 2, 3 }));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            Assert.NotNull(report.Obfuscation);
            Assert.True(report.Obfuscation!.PackerDetected);
            Assert.Contains(report.Obfuscation.Signals, s => s.Name.Contains("360 Jiagu"));
            Assert.Contains(report.Notes, n => n.Category == "obfuscation" && n.Severity == NoteSeverity.Warning);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_real_percentages_from_r8_json()
    {
        var r8 = """
        {
          "version": "9.0.32",
          "isOptimizationsEnabled": true,
          "isRepackageClassesEnabled": true,
          "resourceOptimization": { "isOptimizedShrinkingEnabled": true },
          "stats": {
            "noObfuscationPercentage": 12.4,
            "noOptimizationPercentage": 40,
            "noShrinkingPercentage": 30.6
          }
        }
        """;
        var path = WriteAab(("BUNDLE-METADATA/com.android.tools/r8.json", Encoding.UTF8.GetBytes(r8)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            var o = report.Obfuscation!;

            Assert.True(o.HasR8Metadata);
            Assert.True(o.HasMetrics);
            Assert.Equal(88, o.ObfuscationPercent);   // 100 - 12.4 -> 87.6 -> 88
            Assert.Equal(60, o.OptimizationPercent);  // 100 - 40
            Assert.Equal(69, o.ShrinkingPercent);     // 100 - 30.6 -> 69.4 -> 69
            Assert.Equal("9.0.32", o.R8Version);
            Assert.True(o.R8OptimizationsEnabled);
            Assert.True(o.R8RepackageClassesEnabled);
            Assert.True(o.R8OptimizedResourceShrinkingEnabled);
            Assert.False(string.IsNullOrEmpty(o.R8MetadataRaw));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reports_missing_r8_json_without_guessing()
    {
        var path = WriteApk(); // APKs never carry r8.json
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            var o = report.Obfuscation!;

            Assert.False(o.HasR8Metadata);
            Assert.False(o.HasMetrics);
            Assert.Null(o.ObfuscationPercent);
            Assert.Null(o.OptimizationPercent);
            Assert.Null(o.ShrinkingPercent);
            Assert.Contains(report.Notes, n => n.Category == "obfuscation" && n.Message.Contains("r8.json"));
        }
        finally { File.Delete(path); }
    }
}
