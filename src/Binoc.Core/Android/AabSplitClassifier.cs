using System.Text.RegularExpressions;

namespace Binoc.Core.Android;

/// <summary>
/// Assigns one AAB module entry to the split it would land in when Play generates a device APK set: the
/// <b>master</b> split every device gets, or a per-<b>ABI</b> / per-<b>density</b> / per-<b>language</b> split
/// keyed by its configuration qualifier. This mirrors how bundletool partitions <c>lib/&lt;abi&gt;/</c> and
/// config-qualified <c>res/</c> directories — enough to estimate per-device download size (findings §size),
/// without running bundletool (decision D1).
/// </summary>
public static class AabSplitClassifier
{
    public enum SplitDim { Master, Abi, Density, Language }

    /// <summary>The split an entry belongs to.</summary>
    /// <param name="Dim">Which dimension (or Master).</param>
    /// <param name="Key">The split key — an ABI, density bucket, or language tag; empty for Master.</param>
    public readonly record struct Split(SplitDim Dim, string Key);

    // Density buckets that form their own split. nodpi/anydpi are density-agnostic → delivered to all (master).
    private static readonly HashSet<string> DensityBuckets = new(StringComparer.Ordinal)
    {
        "ldpi", "mdpi", "tvdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi",
    };

    // A language qualifier token ("fr", "fil"); an Android region follows as a separate "-rBR" token, and a
    // BCP-47 locale arrives as a single "b+sr+Latn" token.
    private static readonly Regex Language2Or3 = new(@"^[a-z]{2,3}$", RegexOptions.Compiled);
    private static readonly Regex Region = new(@"^r[A-Za-z]{2,3}$", RegexOptions.Compiled);

    private static readonly Regex ExactDpi = new(@"^\d+dpi$", RegexOptions.Compiled);

    // The config-qualified res directory, e.g. base/res/drawable-xxhdpi-v4/ic.png → "drawable-xxhdpi-v4".
    private static readonly Regex ResDir = new(@"(^|/)res/([^/]+)/", RegexOptions.Compiled);

    // A per-ABI native library, e.g. base/lib/arm64-v8a/libfoo.so → "arm64-v8a".
    private static readonly Regex Lib = new(@"(^|/)lib/([^/]+)/", RegexOptions.Compiled);

    /// <summary>Classifies a normalised ('/'-separated) module entry path.</summary>
    public static Split Classify(string path)
    {
        var lib = Lib.Match(path);
        if (lib.Success) return new Split(SplitDim.Abi, lib.Groups[2].Value);

        var res = ResDir.Match(path);
        if (res.Success)
        {
            // Split the directory into type + qualifiers: "drawable-xxhdpi-v4" → [drawable, xxhdpi, v4].
            var parts = res.Groups[2].Value.Split('-');
            for (int i = 1; i < parts.Length; i++)
            {
                var q = parts[i];
                if (DensityBuckets.Contains(q) || ExactDpi.IsMatch(q)) return new Split(SplitDim.Density, q);
            }
            for (int i = 1; i < parts.Length; i++)
            {
                var q = parts[i];
                if (q.StartsWith("b+", StringComparison.Ordinal))
                    return new Split(SplitDim.Language, q);
                if (Language2Or3.IsMatch(q))
                {
                    // Fold a following Android region qualifier ("-rBR") into the key.
                    if (i + 1 < parts.Length && Region.IsMatch(parts[i + 1]))
                        return new Split(SplitDim.Language, $"{q}-{parts[i + 1]}");
                    return new Split(SplitDim.Language, q);
                }
            }
        }

        return new Split(SplitDim.Master, string.Empty);
    }
}
