namespace Binoc.Core.Model;

/// <summary>
/// Obfuscation / optimisation (findings §Obfuscation). binoc reports only <em>facts</em> here, no guessed
/// numbers: the optimisation / obfuscation / shrinking percentages come straight from R8's own
/// <c>BUNDLE-METADATA/com.android.tools/r8.json</c> (the same source Google Play uses), and packer/protector
/// detection comes from concrete on-disk signatures. When an APK/AAB carries no <c>r8.json</c>, the metrics are
/// simply reported as missing — binoc does not invent a substitute.
/// </summary>
public sealed class ObfuscationInfo
{
    // ── Play metrics, read verbatim from r8.json (whole %, positive form). Null when r8.json is absent. ──

    /// <summary>Obfuscation percentage from r8.json (100 − noObfuscationPercentage).</summary>
    public int? ObfuscationPercent { get; set; }

    /// <summary>Optimisation percentage from r8.json (100 − noOptimizationPercentage).</summary>
    public int? OptimizationPercent { get; set; }

    /// <summary>Shrinking percentage from r8.json (100 − noShrinkingPercentage).</summary>
    public int? ShrinkingPercent { get; set; }

    // ── R8 build metadata (BUNDLE-METADATA/com.android.tools/r8.json). AAB only. ──

    /// <summary>True when the AAB carries R8's <c>r8.json</c> build metadata (the source of the metrics above).</summary>
    public bool HasR8Metadata { get; set; }

    /// <summary>R8 compiler version from r8.json (Play only scores bundles built with a recent-enough R8).</summary>
    public string? R8Version { get; set; }

    /// <summary>Whether R8 ran in full mode (the inverse of ProGuard-compatibility mode), from r8.json. This is
    /// what Play reports as "full mode" — a separate axis from <see cref="R8OptimizationsEnabled"/>.</summary>
    public bool? R8FullMode { get; set; }

    /// <summary>Whether R8 optimisations ran at all (i.e. not <c>-dontoptimize</c>), from r8.json. Not the same as
    /// full mode.</summary>
    public bool? R8OptimizationsEnabled { get; set; }

    /// <summary>Whether R8 repackaged classes into one package, from r8.json.</summary>
    public bool? R8RepackageClassesEnabled { get; set; }

    /// <summary>Whether resource shrinking ran (the traditional AGP <c>shrinkResources</c> pass). r8.json doesn't
    /// record the standalone shrinker directly; this is true when optimised resource shrinking is on (which
    /// requires it), otherwise null. Play lists this separately from optimised resource shrinking.</summary>
    public bool? R8ResourceShrinkingEnabled { get; set; }

    /// <summary>Whether R8's <em>optimised</em> (code-aware) resource shrinking ran, from r8.json. Play lists this
    /// separately from plain resource shrinking.</summary>
    public bool? R8OptimizedResourceShrinkingEnabled { get; set; }

    /// <summary>The raw r8.json content (pretty-printed) for display in the report, when present. Null otherwise.</summary>
    public string? R8MetadataRaw { get; set; }

    /// <summary>True when an R8 deobfuscation map is embedded in the bundle
    /// (<c>BUNDLE-METADATA/com.android.tools.build.obfuscation/proguard.map</c>) — what Play extracts on upload to
    /// de-obfuscate crash stack traces. AAB only; null if unknown.</summary>
    public bool? HasEmbeddedDeobfuscationMap { get; set; }

    // ── Commercial packer/protector detection (concrete signatures, not a heuristic score). ──

    /// <summary>Packer/protector signatures that matched (empty when none).</summary>
    public List<ObfuscationSignal> Signals { get; } = new();

    /// <summary>True when a commercial packer/protector signature matched.</summary>
    public bool PackerDetected => Signals.Any(s => s.Kind == SignalKind.Packer);

    /// <summary>True when R8's metrics are available (r8.json present and parsed).</summary>
    public bool HasMetrics => ObfuscationPercent is not null || OptimizationPercent is not null || ShrinkingPercent is not null;
}

/// <summary>What sort of tool a signal points at.</summary>
public enum SignalKind
{
    /// <summary>A commercial packer/protector (encrypts/wraps the real DEX).</summary>
    Packer,
}

/// <summary>One detection signal: what matched, a human-readable detail, and its kind.</summary>
/// <param name="Name">Short signal name (e.g. "Packer: 360 Jiagu").</param>
/// <param name="Detail">Human-readable evidence.</param>
/// <param name="Kind">The class of tool this points at.</param>
public sealed record ObfuscationSignal(string Name, string Detail, SignalKind Kind);
