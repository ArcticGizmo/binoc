using System.IO.Compression;
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
        foreach (var (name, data) in entries)
            using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        return path;
    }

    // A DEX with `mangled` single-char classes and `readable` normally-named classes.
    private static byte[] Dex(int mangled, int readable)
    {
        var descriptors = new List<string>();
        for (int i = 0; i < mangled; i++) descriptors.Add($"Lcom/x/{(char)('a' + i % 26)}{(i >= 26 ? ((char)('a' + i / 26)).ToString() : "")};");
        for (int i = 0; i < readable; i++) descriptors.Add($"Lcom/example/Screen{i};");
        return DexTestBuilder.Build(descriptors.ToArray());
    }

    [Fact]
    public void Flags_likely_obfuscation_from_mangled_names()
    {
        var path = WriteApk(("classes.dex", Dex(mangled: 26, readable: 4)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Obfuscation);
            var o = report.Obfuscation!;
            Assert.Equal(ObfuscationAssessment.Likely, o.Assessment);
            Assert.False(o.PackerDetected);
            Assert.NotNull(o.MangledNameRatio);
            Assert.True(o.MangledNameRatio > 0.6, $"ratio was {o.MangledNameRatio}");
            Assert.Contains(o.Signals, s => s.Kind == SignalKind.NameMangling);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Detects_commercial_packer()
    {
        var path = WriteApk(
            ("classes.dex", Dex(mangled: 1, readable: 1)),
            ("lib/arm64-v8a/libjiagu.so", new byte[] { 1, 2, 3 }));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Obfuscation);
            var o = report.Obfuscation!;
            Assert.Equal(ObfuscationAssessment.Packed, o.Assessment);
            Assert.True(o.PackerDetected);
            Assert.True(o.Confidence >= 0.9);
            Assert.Contains(o.Signals, s => s.Kind == SignalKind.Packer && s.Name.Contains("360 Jiagu"));
            Assert.Contains(report.Notes, n => n.Category == "obfuscation" && n.Severity == NoteSeverity.Warning);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reports_no_obfuscation_for_readable_names()
    {
        var path = WriteApk(("classes.dex", Dex(mangled: 0, readable: 30)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Obfuscation);
            var o = report.Obfuscation!;
            Assert.Equal(ObfuscationAssessment.None, o.Assessment);
            Assert.False(o.PackerDetected);
            Assert.Equal(0.0, o.MangledNameRatio);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Small_class_set_is_capped_below_likely()
    {
        // 5 mangled of 5 → ratio 1.0, but too small a sample to call "Likely".
        var path = WriteApk(("classes.dex", Dex(mangled: 5, readable: 0)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            var o = report.Obfuscation!;
            Assert.Equal(ObfuscationAssessment.Possible, o.Assessment);
            Assert.True(o.Confidence <= 0.5);
        }
        finally { File.Delete(path); }
    }
}
