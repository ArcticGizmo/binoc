using System.IO.Compression;
using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// AAB per-device sizing (M5). An AAB is a publishing format — Play generates a per-device APK set from it,
/// splitting native libraries by ABI and resources by density and language. This estimates the download a
/// representative device actually receives (master split + its ABI/density/language splits) and the saving
/// versus one universal APK. Sizes are the entries' compressed lengths — a download proxy, clearly labelled as
/// an estimate; binoc doesn't run bundletool (decision D1/D4).
/// </summary>
public sealed class BundleSizeAnalyzer : IAnalyzer
{
    public string Category => "size";

    public bool AppliesTo(BinaryFormat format) => format is BinaryFormat.Aab;

    // Representative device profiles, resolved against the ABIs/densities the bundle actually ships.
    private static readonly (string Profile, string Abi, string Density)[] Profiles =
    {
        ("64-bit flagship", "arm64-v8a", "xxxhdpi"),
        ("64-bit mid-range", "arm64-v8a", "xhdpi"),
        ("32-bit legacy", "armeabi-v7a", "hdpi"),
    };

    // Density ordering for nearest-match when a device's preferred bucket isn't shipped.
    private static readonly string[] DensityRank =
    {
        "ldpi", "mdpi", "tvdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi",
    };

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var master = 0L;
        var abis = new Dictionary<string, long>(StringComparer.Ordinal);
        var densities = new Dictionary<string, long>(StringComparer.Ordinal);
        var languages = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var entry in context.Archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.EndsWith('/')) continue;                                  // directory marker
            if (!IsDeliveredContent(path)) continue;                           // bundle metadata isn't shipped

            long size = entry.CompressedLength;
            var split = AabSplitClassifier.Classify(path);
            switch (split.Dim)
            {
                case AabSplitClassifier.SplitDim.Abi: Add(abis, split.Key, size); break;
                case AabSplitClassifier.SplitDim.Density: Add(densities, split.Key, size); break;
                case AabSplitClassifier.SplitDim.Language: Add(languages, split.Key, size); break;
                default: master += size; break;
            }
        }

        var info = new BundleSizeInfo { MasterBytes = master };

        var config = context.Archive.GetEntry("BundleConfig.pb");
        var dims = BundleConfigReader.Resolve(config is null ? null : ReadFully(config));
        info.SplitDimensions.AddRange(dims.EnabledDimensions);
        info.DimensionsFromConfig = dims.FromConfig;

        foreach (var kv in abis.OrderByDescending(k => k.Value)) info.Abis.Add(new DimensionSplit(kv.Key, kv.Value));
        foreach (var kv in densities.OrderByDescending(k => k.Value)) info.Densities.Add(new DimensionSplit(kv.Key, kv.Value));
        foreach (var kv in languages.OrderByDescending(k => k.Value)) info.Languages.Add(new DimensionSplit(kv.Key, kv.Value));

        info.UniversalApkBytes = master + abis.Values.Sum() + densities.Values.Sum() + languages.Values.Sum();

        BuildDeviceEstimates(info, abis, densities, languages);

        report.BundleSize = info;

        var best = info.Devices.OrderByDescending(d => d.SavingsFraction).FirstOrDefault();
        if (best is not null && best.SavingsFraction > 0.01)
            report.Notes.Add(new ReportNote("size", NoteSeverity.Info,
                $"Estimated per-device download is ~{best.SavingsFraction:P0} smaller than a universal APK "
                + $"({Human(best.DownloadBytes)} vs {Human(info.UniversalApkBytes)}). Compressed-size estimate — "
                + "binoc doesn't run bundletool."));
    }

    private static void BuildDeviceEstimates(
        BundleSizeInfo info,
        Dictionary<string, long> abis, Dictionary<string, long> densities, Dictionary<string, long> languages)
    {
        string lang = PickLanguage(languages);
        long langBytes = lang.Length > 0 && languages.TryGetValue(lang, out var lb) ? lb : 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (profile, prefAbi, prefDensity) in Profiles)
        {
            string abi = ResolveAbi(abis, prefAbi);
            string density = ResolveDensity(densities, prefDensity);

            // Skip profiles that collapse onto an identical (abi,density) row.
            var key = $"{abi}|{density}|{lang}";
            if (!seen.Add(key)) continue;

            long download = info.MasterBytes
                + (abi.Length > 0 && abis.TryGetValue(abi, out var ab) ? ab : 0)
                + (density.Length > 0 && densities.TryGetValue(density, out var db) ? db : 0)
                + langBytes;

            double savings = info.UniversalApkBytes > 0
                ? 1.0 - (double)download / info.UniversalApkBytes
                : 0.0;

            info.Devices.Add(new DeviceSizeEstimate(
                profile, abi.Length > 0 ? abi : "—", density.Length > 0 ? density : "—",
                lang.Length > 0 ? lang : "base", download, Math.Max(0, savings)));
        }
    }

    // Only content that ends up in a generated APK counts toward device size.
    private static bool IsDeliveredContent(string path) =>
        !path.StartsWith("META-INF/", StringComparison.Ordinal)
        && !path.StartsWith("BUNDLE-METADATA/", StringComparison.Ordinal)
        && path != "BundleConfig.pb";

    private static string ResolveAbi(Dictionary<string, long> abis, string preferred)
    {
        if (abis.Count == 0) return string.Empty;
        if (abis.ContainsKey(preferred)) return preferred;
        // Fall back to the smallest present ABI (best-case download for a device that has no exact match).
        return abis.OrderBy(k => k.Value).First().Key;
    }

    private static string ResolveDensity(Dictionary<string, long> densities, string preferred)
    {
        if (densities.Count == 0) return string.Empty;
        if (densities.ContainsKey(preferred)) return preferred;

        int want = Array.IndexOf(DensityRank, preferred);
        if (want < 0) return densities.OrderByDescending(k => k.Value).First().Key;

        // Nearest by rank distance among the buckets actually present.
        return densities.Keys
            .Select(k => (Key: k, Rank: Array.IndexOf(DensityRank, k)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => Math.Abs(x.Rank - want))
            .Select(x => x.Key)
            .DefaultIfEmpty(densities.OrderByDescending(k => k.Value).First().Key)
            .First();
    }

    private static string PickLanguage(Dictionary<string, long> languages)
    {
        if (languages.Count == 0) return string.Empty;
        if (languages.ContainsKey("en")) return "en";
        return languages.OrderByDescending(k => k.Value).First().Key;
    }

    private static void Add(Dictionary<string, long> map, string key, long size) =>
        map[key] = (map.TryGetValue(key, out var v) ? v : 0) + size;

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Human(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes; int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {units[unit]}";
    }
}
