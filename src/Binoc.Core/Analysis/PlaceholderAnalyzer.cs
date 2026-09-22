using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// The M0 stand-in: proves the end-to-end loop (file → detect → pipeline → report → UI) without parsing
/// anything yet. It records the entry count and a note naming the detected format. It is replaced/joined by
/// the real archive-walk and identity analysers at M1.
/// </summary>
public sealed class PlaceholderAnalyzer : IAnalyzer
{
    public string Category => "general";

    public bool AppliesTo(BinaryFormat format) => true;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        int entries = context.Archive.Entries.Count;
        string formatName = context.Format switch
        {
            BinaryFormat.Apk => "Android APK",
            BinaryFormat.Aab => "Android App Bundle (AAB)",
            BinaryFormat.Ipa => "iOS app archive (IPA)",
            _ => "unrecognised archive",
        };

        report.Notes.Add(new ReportNote(
            Category,
            context.Format == BinaryFormat.Unknown ? NoteSeverity.Warning : NoteSeverity.Info,
            $"Detected {formatName} — {entries:N0} archive {(entries == 1 ? "entry" : "entries")}. "
            + "Detailed analysis lands in later milestones."));
    }
}
