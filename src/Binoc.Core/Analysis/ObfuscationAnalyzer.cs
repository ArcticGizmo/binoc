using System.IO.Compression;
using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Obfuscation / optimisation (Android). Reports facts, not guesses: the optimisation / obfuscation / shrinking
/// percentages are read straight from R8's <c>BUNDLE-METADATA/com.android.tools/r8.json</c> (the same file Google
/// Play reads), and packer/protector detection comes from concrete on-disk signatures (<see cref="PackerSignatures"/>).
/// When an AAB has no <c>r8.json</c> (older AGP/R8 or R8 off) — and always for APKs, which never carry it — the
/// metrics are reported as missing rather than estimated.
/// </summary>
public sealed class ObfuscationAnalyzer : IAnalyzer
{
    public string Category => "obfuscation";

    public bool AppliesTo(BinaryFormat format) => format is BinaryFormat.Apk or BinaryFormat.Aab;

    // r8.json is a few KB in practice; cap defensively before reading it for display.
    private const long MaxR8JsonBytes = 4L * 1024 * 1024;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var paths = context.Archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        var info = new ObfuscationInfo();

        // Commercial packer/protector signatures (concrete evidence).
        foreach (var m in PackerSignatures.Find(paths))
            info.Signals.Add(new ObfuscationSignal(
                $"Packer: {m.Product}", $"protector artefact “{m.Evidence}” present", SignalKind.Packer));

        // R8 build metadata + deobfuscation map — both live in the AAB's BUNDLE-METADATA. AAB only.
        if (context.Format == BinaryFormat.Aab)
        {
            info.HasEmbeddedDeobfuscationMap = paths.Any(p =>
                p.EndsWith("com.android.tools.build.obfuscation/proguard.map", StringComparison.OrdinalIgnoreCase));

            var r8Entry = context.Archive.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').EndsWith(R8MetadataReader.EntrySuffix, StringComparison.OrdinalIgnoreCase));
            if (r8Entry is not null && r8Entry.Length <= MaxR8JsonBytes)
            {
                var bytes = ReadFully(r8Entry);
                info.R8MetadataRaw = R8MetadataReader.PrettyPrint(bytes);
                if (R8MetadataReader.Parse(bytes) is { } md)
                {
                    info.HasR8Metadata = true;
                    info.R8Version = md.Version;
                    info.R8OptimizationsEnabled = md.OptimizationsEnabled;
                    info.R8RepackageClassesEnabled = md.RepackageClassesEnabled;
                    info.R8OptimizedResourceShrinkingEnabled = md.OptimizedResourceShrinkingEnabled;
                    info.ObfuscationPercent = md.ObfuscationPercent;
                    info.OptimizationPercent = md.OptimizationPercent;
                    info.ShrinkingPercent = md.ShrinkingPercent;
                }
            }
        }

        report.Obfuscation = info;
        AddNotes(info, report);
    }

    private static void AddNotes(ObfuscationInfo info, AnalysisReport report)
    {
        if (info.PackerDetected)
        {
            var packers = string.Join(", ", info.Signals.Where(s => s.Kind == SignalKind.Packer)
                .Select(s => s.Name["Packer: ".Length..]));
            report.Notes.Add(new ReportNote("obfuscation", NoteSeverity.Warning,
                $"A commercial packer/protector was detected ({packers}). The real code is unpacked at runtime, so "
                + "static analysis of the DEX will be incomplete."));
        }

        if (info.HasMetrics)
        {
            int min = new[] { info.ObfuscationPercent, info.OptimizationPercent, info.ShrinkingPercent }
                .Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty(100).Min();
            if (min < 25)
                report.Notes.Add(new ReportNote("obfuscation", NoteSeverity.Warning,
                    "An R8 optimisation metric is below 25% — Play enforces a 25% floor from Feb 2027 for apps with "
                    + "non-negligible DEX."));
        }
        else
        {
            report.Notes.Add(new ReportNote("obfuscation", NoteSeverity.Info,
                "No r8.json in this file, so optimisation/obfuscation/shrinking percentages aren't available "
                + "(it's embedded only in AABs built with AGP 8.10+ / recent R8). binoc doesn't estimate them."));
        }
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
