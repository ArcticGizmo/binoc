using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Buckets an archive entry into a human-readable size category, per format. The rules are an ordered list
/// (first match wins) so the specific patterns (native libs, dex, signatures) win over the "Other" catch-all.
/// Paths are matched already-normalised to '/' separators and are case-sensitive except where a format is
/// conventionally case-insensitive.
/// </summary>
internal static class EntryClassifier
{
    public static string Classify(BinaryFormat format, string path) => format switch
    {
        BinaryFormat.Apk => ClassifyApk(path),
        BinaryFormat.Aab => ClassifyAab(path),
        BinaryFormat.Ipa => ClassifyIpa(path),
        _ => "Other",
    };

    private static string ClassifyApk(string p)
    {
        if (p.EndsWith(".dex", StringComparison.Ordinal)) return "DEX code";
        if (p.StartsWith("lib/", StringComparison.Ordinal)) return "Native libraries";
        if (p == "resources.arsc" || p.StartsWith("res/", StringComparison.Ordinal)) return "Resources";
        if (p.StartsWith("assets/", StringComparison.Ordinal)) return "Assets";
        if (p.StartsWith("META-INF/", StringComparison.Ordinal)) return "Signatures & metadata";
        if (p == "AndroidManifest.xml") return "Manifest";
        return "Other";
    }

    private static string ClassifyAab(string p)
    {
        // AAB is per-module: base/, feature modules, plus bundle-level metadata.
        if (p.Contains("/dex/", StringComparison.Ordinal) || p.EndsWith(".dex", StringComparison.Ordinal)) return "DEX code";
        if (p.Contains("/lib/", StringComparison.Ordinal)) return "Native libraries";
        if (p.Contains("/res/", StringComparison.Ordinal) || p.EndsWith("/resources.pb", StringComparison.Ordinal)) return "Resources";
        if (p.Contains("/assets/", StringComparison.Ordinal)) return "Assets";
        if (p.StartsWith("BUNDLE-METADATA/", StringComparison.Ordinal) || p == "BundleConfig.pb") return "Bundle metadata";
        if (p.StartsWith("META-INF/", StringComparison.Ordinal)) return "Signatures & metadata";
        if (p.EndsWith("/manifest/AndroidManifest.xml", StringComparison.Ordinal)) return "Manifest";
        return "Other";
    }

    private static string ClassifyIpa(string p)
    {
        if (p.Contains("/Frameworks/", StringComparison.Ordinal)) return "Frameworks";
        if (p.Contains("/PlugIns/", StringComparison.Ordinal)) return "App extensions";
        if (p.Contains("/Watch/", StringComparison.Ordinal)) return "Watch app";
        if (p.Contains("/_CodeSignature/", StringComparison.Ordinal)) return "Code signature";
        if (p.EndsWith(".car", StringComparison.Ordinal)) return "Asset catalogs";
        if (p.EndsWith(".nib", StringComparison.Ordinal) || p.EndsWith(".storyboardc", StringComparison.Ordinal)
            || p.EndsWith(".plist", StringComparison.Ordinal) || p.EndsWith(".strings", StringComparison.Ordinal)
            || p.Contains(".lproj/", StringComparison.Ordinal)) return "Resources";
        if (p.StartsWith("Payload/", StringComparison.Ordinal) && p.Contains(".app/", StringComparison.Ordinal)) return "App payload";
        return "Other";
    }
}
