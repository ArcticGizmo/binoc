using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Obfuscation / packing (M5, Android). Two independent evidence streams feed one hedged verdict (decision
/// D2/D4): a curated <see cref="PackerSignatures"/> scan of the archive (the strongest signal — a commercial
/// protector), and a name-mangling ratio over the app's own defined classes via <see cref="DexTypeReader"/>
/// (R8/ProGuard/DexGuard rename most classes to 1–2 char names). The result is always a confidence + the
/// signals behind it, never a bare yes/no.
/// </summary>
public sealed class ObfuscationAnalyzer : IAnalyzer
{
    public string Category => "obfuscation";

    public bool AppliesTo(BinaryFormat format) => format is BinaryFormat.Apk or BinaryFormat.Aab;

    // APK: classes.dex at the root. AAB: <module>/dex/classes*.dex.
    private static readonly Regex DexEntry =
        new(@"(^|/)classes\d*\.dex$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Deep DEX read decompresses the whole file; skip (and lower confidence) past this to stay bounded.
    private const long MaxDexBytes = 64L * 1024 * 1024;

    // Below this many defined classes the ratio is too small a sample to call "Likely".
    private const int MinSampleForConfidence = 25;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var paths = context.Archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        var dexEntries = context.Archive.Entries
            .Where(e => DexEntry.IsMatch(e.FullName.Replace('\\', '/')))
            .ToList();

        // No code to reason about → nothing to say.
        if (dexEntries.Count == 0) return;

        var info = new ObfuscationInfo();

        // ── Stream 1: commercial packer/protector signatures (strongest signal) ──
        foreach (var m in PackerSignatures.Find(paths))
            info.Signals.Add(new ObfuscationSignal(
                $"Packer: {m.Product}", $"protector artefact “{m.Evidence}” present", SignalKind.Packer, 0.95));

        // R8's mapping.txt embedded in the bundle → this is what Play extracts on upload to de-obfuscate
        // crash traces, no separate deobfuscation upload needed. AAB only.
        if (context.Format == BinaryFormat.Aab)
            info.HasEmbeddedDeobfuscationMap = paths.Any(p =>
                p.EndsWith("com.android.tools.build.obfuscation/proguard.map", StringComparison.OrdinalIgnoreCase));

        // ── Stream 2: name-mangling ratio over defined symbols (+ repackaging fingerprint) ──
        int classes = 0, symbols = 0, mangledSymbols = 0, skippedDex = 0;
        var packages = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in dexEntries)
        {
            if (entry.Length > MaxDexBytes) { skippedDex++; continue; }
            var stats = DexTypeReader.Read(ReadFully(entry));
            if (stats is not { } s) continue;
            classes += s.DefinedClasses;
            symbols += s.DefinedSymbols;
            mangledSymbols += s.MangledSymbols;
            if (s.TopPackageCount > 0)
                packages[s.TopPackage] = (packages.TryGetValue(s.TopPackage, out var c) ? c : 0) + s.TopPackageCount;
        }

        if (symbols > 0)
        {
            double ratio = (double)mangledSymbols / symbols;
            info.MangledNameRatio = ratio;
            info.SymbolsSampled = symbols;
            info.ClassesSampled = classes;

            if (ratio >= 0.30)
                info.Signals.Add(new ObfuscationSignal(
                    "Renamed symbols",
                    $"{mangledSymbols:N0} of {symbols:N0} defined symbols ({ratio:P0}) use short machine names",
                    SignalKind.NameMangling, ratio));

            // Repackaging: almost every class in one short/empty package.
            var top = packages.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (classes >= MinSampleForConfidence)
            {
                double share = (double)top.Value / classes;
                bool repackaged = share >= 0.80 && (top.Key?.Length ?? 0) <= 3;
                info.RepackagedClasses = repackaged;
                if (repackaged)
                    info.Signals.Add(new ObfuscationSignal(
                        "Repackaged classes",
                        $"{share:P0} of classes collapsed into one package (“{(top.Key!.Length == 0 ? "<root>" : top.Key)}”) "
                        + "— consistent with R8 -repackageclasses / full mode",
                        SignalKind.NameMangling, Math.Min(1.0, share)));
            }
        }

        if (skippedDex > 0)
            info.Signals.Add(new ObfuscationSignal(
                "DEX too large to inspect",
                $"{skippedDex} DEX file(s) exceeded the {MaxDexBytes / (1024 * 1024)} MB deep-read limit; the name ratio may understate obfuscation",
                SignalKind.Hint, 0.0));

        Score(info, symbols);

        // Nothing worth surfacing (readable names, no packer) — drop the category.
        if (info.Assessment == ObfuscationAssessment.None && !info.PackerDetected && info.MangledNameRatio is null)
            return;

        report.Obfuscation = info;
        AddNotes(info, report);
    }

    // Turns the collected signals into the coarse assessment + confidence.
    private static void Score(ObfuscationInfo info, int sampleSize)
    {
        if (info.PackerDetected)
        {
            info.Assessment = ObfuscationAssessment.Packed;
            info.Confidence = info.Signals.Where(s => s.Kind == SignalKind.Packer).Max(s => s.Weight);
            return;
        }

        double ratio = info.MangledNameRatio ?? 0.0;
        info.Confidence = ratio;

        if (ratio >= 0.60) info.Assessment = ObfuscationAssessment.Likely;
        else if (ratio >= 0.30) info.Assessment = ObfuscationAssessment.Possible;
        else info.Assessment = ObfuscationAssessment.None;

        // A small symbol set can't carry a strong verdict — cap it and reflect that in the confidence.
        if (sampleSize is > 0 and < MinSampleForConfidence && info.Assessment == ObfuscationAssessment.Likely)
        {
            info.Assessment = ObfuscationAssessment.Possible;
            info.Confidence = Math.Min(info.Confidence, 0.5);
        }
    }

    private static void AddNotes(ObfuscationInfo info, AnalysisReport report)
    {
        switch (info.Assessment)
        {
            case ObfuscationAssessment.Packed:
                var packers = string.Join(", ", info.Signals.Where(s => s.Kind == SignalKind.Packer)
                    .Select(s => s.Name["Packer: ".Length..]));
                report.Notes.Add(new ReportNote("obfuscation", NoteSeverity.Warning,
                    $"A commercial packer/protector was detected ({packers}). The real code is unpacked at "
                    + "runtime, so static analysis of the DEX will be incomplete."));
                break;
            case ObfuscationAssessment.Likely:
                report.Notes.Add(new ReportNote("obfuscation", NoteSeverity.Info,
                    $"Code looks obfuscated ({info.MangledNameRatio:P0} of symbols renamed, confidence "
                    + $"{info.Confidence:P0}) — consistent with a normal R8/ProGuard release."));
                break;
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
