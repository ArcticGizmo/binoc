namespace Binoc.Core.Model;

/// <summary>
/// AAB per-device sizing (findings §Optimisation/size, decision D4): an AAB isn't installed as-is — Play
/// generates a per-device APK set from it, splitting by ABI × screen-density × language. This estimates the
/// <em>download</em> a representative device actually receives, and the saving versus shipping one universal
/// APK. Sizes are compressed-length proxies, clearly labelled as estimates — binoc doesn't run bundletool.
/// </summary>
public sealed class BundleSizeInfo
{
    /// <summary>The split dimensions in effect (e.g. "ABI", "Screen density", "Language").</summary>
    public List<string> SplitDimensions { get; } = new();

    /// <summary>True when the dimensions were read from <c>BundleConfig.pb</c>; false when defaults were
    /// assumed because no config was present or parseable.</summary>
    public bool DimensionsFromConfig { get; set; }

    /// <summary>Estimated size of a single universal APK (everything, every ABI/density/language) — the
    /// baseline the per-device splits improve on.</summary>
    public long UniversalApkBytes { get; set; }

    /// <summary>The master split every device receives regardless of configuration (code, manifest,
    /// config-agnostic resources and assets).</summary>
    public long MasterBytes { get; set; }

    /// <summary>Per-ABI native-library split sizes.</summary>
    public List<DimensionSplit> Abis { get; } = new();

    /// <summary>Per-density resource split sizes.</summary>
    public List<DimensionSplit> Densities { get; } = new();

    /// <summary>Per-language resource split sizes.</summary>
    public List<DimensionSplit> Languages { get; } = new();

    /// <summary>Estimated downloads for a handful of representative device profiles.</summary>
    public List<DeviceSizeEstimate> Devices { get; } = new();
}

/// <summary>One split within a dimension (an ABI, a density bucket, or a language) and its combined size.</summary>
/// <param name="Name">The split's key (e.g. "arm64-v8a", "xxhdpi", "fr").</param>
/// <param name="Bytes">Combined compressed size of entries in this split.</param>
public sealed record DimensionSplit(string Name, long Bytes);

/// <summary>An estimated download for one representative device profile.</summary>
/// <param name="Profile">Human-readable profile name (e.g. "64-bit flagship").</param>
/// <param name="Abi">The ABI split chosen for this device.</param>
/// <param name="Density">The density split chosen for this device.</param>
/// <param name="Language">The language split chosen for this device.</param>
/// <param name="DownloadBytes">Estimated download: master + the chosen ABI/density/language splits.</param>
/// <param name="SavingsFraction">Fraction saved versus the universal APK (0.0–1.0).</param>
public sealed record DeviceSizeEstimate(
    string Profile, string Abi, string Density, string Language, long DownloadBytes, double SavingsFraction);
