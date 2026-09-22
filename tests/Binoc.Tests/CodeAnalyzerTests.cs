using System.Buffers.Binary;
using System.IO.Compression;
using Binoc.Core.Analysis;
using Xunit;

namespace Binoc.Tests;

public class CodeAnalyzerTests
{
    // A minimal 112-byte DEX header with the given method-ref and class-def counts.
    private static byte[] DexHeader(int methodIds, int classDefs, int stringIds = 100)
    {
        var h = new byte[112];
        new byte[] { 0x64, 0x65, 0x78, 0x0a, 0x30, 0x33, 0x35, 0x00 }.CopyTo(h, 0); // "dex\n035\0"
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(56), (uint)stringIds); // string_ids_size
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(64), 50);              // type_ids_size
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(80), 40);              // field_ids_size
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(88), (uint)methodIds); // method_ids_size
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(96), (uint)classDefs); // class_defs_size
        return h;
    }

    private static string WriteApk(params (string name, byte[] data)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-code-{Guid.NewGuid():N}.apk");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        // Make it detect as an APK.
        using (var m = zip.CreateEntry("AndroidManifest.xml").Open()) m.Write(new byte[] { 0 });
        foreach (var (name, data) in entries)
            using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        return path;
    }

    [Fact]
    public void Counts_multidex_methods_and_classes()
    {
        var path = WriteApk(
            ("classes.dex", DexHeader(methodIds: 60_000, classDefs: 5_000)),
            ("classes2.dex", DexHeader(methodIds: 12_345, classDefs: 800)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.Code);
            Assert.Equal(2, report.Code!.DexFileCount);
            Assert.True(report.Code.MultiDex);
            Assert.Equal(72_345, report.Code.TotalMethodRefs);
            Assert.Equal(5_800, report.Code.TotalDefinedClasses);
            Assert.Equal(60_000, report.Code.MaxMethodRefsInADex);
            Assert.True(report.Code.NearMethodLimit);
            Assert.Equal("classes.dex", report.Code.Dex[0].Name); // sorted by method count desc
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Single_dex_is_not_multidex()
    {
        var path = WriteApk(("classes.dex", DexHeader(methodIds: 1000, classDefs: 100)));
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            Assert.Equal(1, report.Code!.DexFileCount);
            Assert.False(report.Code.MultiDex);
            Assert.False(report.Code.NearMethodLimit);
        }
        finally { File.Delete(path); }
    }
}
