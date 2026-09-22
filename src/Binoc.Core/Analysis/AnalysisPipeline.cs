using System.IO.Compression;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// The orchestrator: detects the format, opens the archive once, and runs each applicable analyzer in
/// order, accumulating into a single <see cref="AnalysisReport"/>. A pass that throws is isolated — its
/// failure becomes an error note and the remaining passes still run (decision D3).
///
/// <para>This is the one entry point every head (the Avalonia app now, a CLI later) calls. It is
/// synchronous and does no I/O beyond reading the file, so it stays trivially testable.</para>
/// </summary>
public static class AnalysisPipeline
{
    /// <summary>The registered passes, in run order. M2+ append signing/provisioning/etc. here.</summary>
    private static readonly IReadOnlyList<IAnalyzer> Analyzers = new IAnalyzer[]
    {
        new ArchiveWalkAnalyzer(),
        new IdentityAnalyzer(),
        new SigningAnalyzer(),
        new CodeAnalyzer(),
        new NativeLibsAnalyzer(),
        new ProvisioningAnalyzer(),
        new MachOAnalyzer(),
    };

    /// <summary>Analyse the file at <paramref name="path"/> and return a fully-populated report. Throws
    /// <see cref="FileNotFoundException"/> if the file is missing; every other failure is captured as a
    /// note on the returned report rather than thrown.</summary>
    public static AnalysisReport Analyze(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("File to analyse was not found.", path);

        var info = new FileInfo(path);
        var format = FormatDetector.Detect(path);

        var report = new AnalysisReport
        {
            FilePath = Path.GetFullPath(path),
            FileName = info.Name,
            FileSizeBytes = info.Length,
            Format = format,
        };

        if (format == BinaryFormat.Unknown)
        {
            report.Notes.Add(new ReportNote("general", NoteSeverity.Error,
                "This file isn't a recognised APK, AAB or IPA (it isn't a ZIP archive, or its contents "
                + "match none of the three formats)."));
            return report;
        }

        // All three formats are ZIPs — open once and share the handle across passes.
        using var zip = ZipFile.OpenRead(path);
        var context = new AnalysisContext { Format = format, Archive = zip, FilePath = report.FilePath };

        foreach (var analyzer in Analyzers)
        {
            if (!analyzer.AppliesTo(format)) continue;
            try
            {
                analyzer.Analyze(context, report);
            }
            catch (Exception ex)
            {
                report.Notes.Add(new ReportNote(analyzer.Category, NoteSeverity.Error,
                    $"The {analyzer.Category} analysis failed and was skipped: {ex.Message}"));
            }
        }

        return report;
    }
}
