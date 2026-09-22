namespace Binoc.Core.Model;

/// <summary>
/// Obfuscation / packing (findings §Obfuscation, decision D2/D4): a deliberately <em>hedged</em> read of
/// whether the app's code has been renamed (R8/ProGuard/DexGuard) or wrapped by a commercial packer/protector
/// (360 Jiagu, Bangcle, Tencent Legu, …). This is never a bare yes/no — it carries a confidence score and the
/// individual signals that produced it, so a reader can judge the evidence themselves.
/// </summary>
public sealed class ObfuscationInfo
{
    /// <summary>The headline verdict, coarsened from <see cref="Confidence"/> and the signal set.</summary>
    public ObfuscationAssessment Assessment { get; set; }

    /// <summary>Confidence that the app is obfuscated/packed, 0.0–1.0. Never presented as certainty.</summary>
    public double Confidence { get; set; }

    /// <summary>The evidence — each signal that fired, with a human-readable detail and its weight.</summary>
    public List<ObfuscationSignal> Signals { get; } = new();

    /// <summary>Fraction of the app's own defined classes whose simple name looks machine-renamed
    /// (e.g. <c>a</c>, <c>b</c>, <c>ab</c>) — the core name-mangling signal, or null when the DEX couldn't be
    /// read deeply.</summary>
    public double? MangledNameRatio { get; set; }

    /// <summary>How many defined classes the ratio was computed over (0 when not computed).</summary>
    public int ClassesSampled { get; set; }

    /// <summary>True when a commercial packer/protector signature matched — the strongest single signal.</summary>
    public bool PackerDetected => Signals.Any(s => s.Kind == SignalKind.Packer);
}

/// <summary>The coarse obfuscation verdict, ordered least-to-most transformed.</summary>
public enum ObfuscationAssessment
{
    /// <summary>No meaningful obfuscation signals — names look like readable source.</summary>
    None,
    /// <summary>A minority of names are mangled, or only weak signals fired.</summary>
    Possible,
    /// <summary>Most of the app's classes are renamed — consistent with a normal R8/ProGuard release.</summary>
    Likely,
    /// <summary>A commercial packer/protector wraps the code (DEX encryption/anti-tamper).</summary>
    Packed,
}

/// <summary>What sort of tool a signal points at, driving how heavily it weighs on the verdict.</summary>
public enum SignalKind
{
    /// <summary>Name mangling — consistent with R8/ProGuard/DexGuard.</summary>
    NameMangling,
    /// <summary>A commercial packer/protector (encrypts/wraps the real DEX).</summary>
    Packer,
    /// <summary>A weaker corroborating hint (e.g. a stripped/absent debug marker).</summary>
    Hint,
}

/// <summary>One obfuscation signal: what fired, a human-readable detail, and how much to trust it.</summary>
/// <param name="Name">Short signal name (e.g. "Renamed classes", "Packer: 360 Jiagu").</param>
/// <param name="Detail">Human-readable evidence.</param>
/// <param name="Kind">The class of tool this points at.</param>
/// <param name="Weight">Relative contribution to confidence, 0.0–1.0.</param>
public sealed record ObfuscationSignal(string Name, string Detail, SignalKind Kind, double Weight);
