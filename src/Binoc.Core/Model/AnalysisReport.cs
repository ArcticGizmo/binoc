namespace Binoc.Core.Model;

/// <summary>
/// The normalised, format-agnostic result of analysing one binary — binoc's single output model across
/// APK, AAB and IPA (findings doc §4, decision D2). The 8 shared categories from the research map onto the
/// nullable properties here; a category is <c>null</c> until an analyser fills it (and stays null where a
/// format has no such data — e.g. <see cref="Provisioning"/> is iOS-only).
///
/// <para>M0 populates only <see cref="Format"/> + the file-level fields and appends a <see cref="Notes"/>
/// entry; the category slots are defined now so later milestones (M1+) grow into them without reshaping
/// the model. Serialised to the stable JSON schema at M6.</para>
/// </summary>
public sealed class AnalysisReport
{
    /// <summary>Absolute path to the analysed file.</summary>
    public required string FilePath { get; init; }

    /// <summary>The file's name (no directory).</summary>
    public required string FileName { get; init; }

    /// <summary>Size of the file on disk, in bytes.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>The detected format (see <see cref="FormatDetector"/>).</summary>
    public BinaryFormat Format { get; init; }

    // ── The 8 shared categories (findings §"The 8 shared categories"). Filled from M1 on. ──

    /// <summary>Entry counts, compressed/uncompressed totals, per-type size breakdown. (M1)</summary>
    public ArchiveInfo? Archive { get; set; }

    /// <summary>Package/bundle id, versions, SDK levels, debug-vs-release, build time. (M1)</summary>
    public IdentityInfo? Identity { get; set; }

    /// <summary>Signers, certificate fingerprints + validity, signing schemes, debug-key flag. (M2)</summary>
    public SigningInfo? Signing { get; set; }

    /// <summary>DEX picture — counts vs the 64K method limit, multidex (Android APK/AAB). (M3)</summary>
    public CodeInfo? Code { get; set; }

    // Later milestones add: Provisioning (iOS), Code, Obfuscation, SizeBreakdown, NativeLibs,
    // SecurityPosture — each as its own nullable category object, following the same shape.

    /// <summary>Per-category notes: informational context, degradation warnings, and hard errors from
    /// analysers that failed (decision D3 — a failed pass records here and the rest still run).</summary>
    public List<ReportNote> Notes { get; } = new();
}

/// <summary>Severity of a <see cref="ReportNote"/>, driving how it's surfaced in the report UI.</summary>
public enum NoteSeverity { Info, Warning, Error }

/// <summary>One line of context attached to a report — tagged with the category it belongs to so the UI
/// can group it, and a severity so posture/degradation reads at a glance.</summary>
/// <param name="Category">The category this note relates to (e.g. "identity", "signing", or "general").</param>
/// <param name="Severity">How prominently to surface it.</param>
/// <param name="Message">Human-readable text.</param>
public sealed record ReportNote(string Category, NoteSeverity Severity, string Message);
