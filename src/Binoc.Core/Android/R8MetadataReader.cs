using System.Text.Json;

namespace Binoc.Core.Android;

/// <summary>
/// Reads R8's build metadata embedded in an AAB at <c>BUNDLE-METADATA/com.android.tools/r8.json</c>. This is the
/// same file Google Play reads (AGP 8.10+ / recent R8) to report its optimisation / obfuscation / shrinking
/// figures — R8 records its own settings there at build time, so the "before" Play needs is captured in the
/// bundle rather than requiring a separate pre-shrink build. We surface the R8 version and the config flags that
/// drive those figures. Tolerant: unknown/missing fields are simply left null (decision D1 — BCL only).
/// </summary>
public static class R8MetadataReader
{
    /// <summary>R8 build metadata, as far as binoc reads it. Any field may be null when absent/unrecognised. The
    /// three percentages are the positive form (<c>100 − stats.noXxxPercentage</c>), rounded to a whole number —
    /// the exact figures Google Play reports.</summary>
    /// <param name="Version">R8 compiler version string (Play gates on this being recent enough to be scored).</param>
    /// <param name="OptimizationsEnabled">Whether R8 optimisations ran (roughly "full mode" optimisation).</param>
    /// <param name="RepackageClassesEnabled">Whether classes were repackaged into one package.</param>
    /// <param name="OptimizedResourceShrinkingEnabled">Whether R8's optimised resource shrinking ran.</param>
    /// <param name="ObfuscationPercent">Obfuscation % (100 − stats.noObfuscationPercentage), rounded.</param>
    /// <param name="OptimizationPercent">Optimisation % (100 − stats.noOptimizationPercentage), rounded.</param>
    /// <param name="ShrinkingPercent">Shrinking % (100 − stats.noShrinkingPercentage), rounded.</param>
    public readonly record struct R8Metadata(
        string? Version, bool? OptimizationsEnabled, bool? RepackageClassesEnabled, bool? OptimizedResourceShrinkingEnabled,
        int? ObfuscationPercent, int? OptimizationPercent, int? ShrinkingPercent);

    /// <summary>The AAB path R8 writes its metadata to.</summary>
    public const string EntrySuffix = "com.android.tools/r8.json";

    /// <summary>Parses r8.json, or null if it isn't valid JSON.</summary>
    public static R8Metadata? Parse(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            bool? optResShrink = null;
            if (root.TryGetProperty("resourceOptimization", out var ro) && ro.ValueKind == JsonValueKind.Object)
                optResShrink = GetBool(ro, "isOptimizedShrinkingEnabled");

            int? obf = null, opt = null, shr = null;
            if (root.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
            {
                obf = Positive(stats, "noObfuscationPercentage");
                opt = Positive(stats, "noOptimizationPercentage");
                shr = Positive(stats, "noShrinkingPercentage");
            }

            return new R8Metadata(
                GetString(root, "version"),
                GetBool(root, "isOptimizationsEnabled"),
                GetBool(root, "isRepackageClassesEnabled"),
                optResShrink,
                obf, opt, shr);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Reads a "noXxxPercentage" number and returns the positive form (100 − it), rounded and clamped to 0–100.
    private static int? Positive(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (!v.TryGetDouble(out double no)) return null;
        int pct = (int)Math.Round(100.0 - no, MidpointRounding.AwayFromZero);
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>Returns r8.json pretty-printed for display, or the raw text if it isn't valid JSON, or null.</summary>
    public static string? PrettyPrint(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            try { return System.Text.Encoding.UTF8.GetString(json); }
            catch { return null; }
        }
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? GetBool(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
