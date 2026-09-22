using System.IO.Compression;
using Binoc.Core.Analysis;
using Xunit;

namespace Binoc.Tests;

public class BundleSizeAnalyzerTests
{
    private static byte[] Blob(int bytes)
    {
        var b = new byte[bytes];
        new Random(bytes).NextBytes(b); // incompressible-ish, so compressed length is meaningful
        return b;
    }

    private static string WriteAab()
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-size-{Guid.NewGuid():N}.aab");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        void Entry(string name, byte[] data) { using var s = zip.CreateEntry(name).Open(); s.Write(data); }

        Entry("BundleConfig.pb", new byte[] { 0 });                          // present → detects AAB; not sized
        Entry("base/manifest/AndroidManifest.xml", Blob(2_000));             // master
        Entry("base/dex/classes.dex", Blob(20_000));                         // master
        Entry("base/lib/arm64-v8a/libnative.so", Blob(40_000));             // ABI split
        Entry("base/lib/armeabi-v7a/libnative.so", Blob(30_000));           // ABI split
        Entry("base/res/drawable-xxhdpi/hero.png", Blob(16_000));          // density split
        Entry("base/res/drawable-hdpi/hero.png", Blob(8_000));             // density split
        Entry("base/res/values-fr/strings.xml", Blob(4_000));              // language split
        Entry("base/res/values/strings.xml", Blob(4_000));                 // master (default locale)
        Entry("META-INF/MANIFEST.MF", Blob(1_000));                         // not delivered → excluded
        return path;
    }

    [Fact]
    public void Estimates_per_device_size_and_savings()
    {
        var path = WriteAab();
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.BundleSize);
            var b = report.BundleSize!;

            // Splits were found across all three dimensions.
            Assert.Contains(b.Abis, s => s.Name == "arm64-v8a");
            Assert.Contains(b.Abis, s => s.Name == "armeabi-v7a");
            Assert.Contains(b.Densities, s => s.Name == "xxhdpi");
            Assert.Contains(b.Densities, s => s.Name == "hdpi");
            Assert.Contains(b.Languages, s => s.Name == "fr");

            // Default dimensions (config was unparseable/empty).
            Assert.Contains("ABI", b.SplitDimensions);
            Assert.False(b.DimensionsFromConfig);

            // A device downloads less than the universal APK (it gets one ABI, not both).
            Assert.NotEmpty(b.Devices);
            foreach (var d in b.Devices)
                Assert.True(d.DownloadBytes < b.UniversalApkBytes, "per-device should be below universal");

            var flagship = Assert.Single(b.Devices, d => d.Profile == "64-bit flagship");
            Assert.Equal("arm64-v8a", flagship.Abi);
            Assert.True(flagship.SavingsFraction > 0);

            // META-INF wasn't counted toward the universal size.
            long delivered = b.MasterBytes + b.Abis.Sum(x => x.Bytes) + b.Densities.Sum(x => x.Bytes) + b.Languages.Sum(x => x.Bytes);
            Assert.Equal(delivered, b.UniversalApkBytes);
        }
        finally { File.Delete(path); }
    }
}
