using System.IO.Compression;
using Binoc.Core.Analysis;
using Binoc.Core.Model;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// The M0 end-to-end loop through <see cref="AnalysisPipeline"/>: detect → open → run passes → report.
/// </summary>
public class AnalysisPipelineTests
{
    private static string WriteTempZip(string extension, params string[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"binoc-test-{Guid.NewGuid():N}{extension}");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var name in entries)
        {
            var entry = zip.CreateEntry(name);
            using var w = new StreamWriter(entry.Open());
            w.Write("x");
        }
        return path;
    }

    [Fact]
    public void Analyze_populates_format_and_file_fields_for_an_apk()
    {
        var path = WriteTempZip(".apk", "AndroidManifest.xml", "classes.dex");
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.Equal(BinaryFormat.Apk, report.Format);
            Assert.Equal(Path.GetFileName(path), report.FileName);
            Assert.True(report.FileSizeBytes > 0);

            Assert.NotNull(report.Archive);
            Assert.Equal(2, report.Archive!.EntryCount);
            Assert.Contains(report.Archive.Buckets, b => b.Label == "DEX code");
            Assert.Contains(report.Archive.Buckets, b => b.Label == "Manifest");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Analyze_flags_an_unrecognised_file()
    {
        var path = WriteTempZip(".zip", "readme.txt");
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.Equal(BinaryFormat.Unknown, report.Format);
            Assert.Contains(report.Notes, n => n.Severity == NoteSeverity.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Analyze_throws_for_a_missing_file()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"binoc-missing-{Guid.NewGuid():N}.apk");
        Assert.Throws<FileNotFoundException>(() => AnalysisPipeline.Analyze(missing));
    }
}
