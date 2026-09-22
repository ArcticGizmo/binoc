namespace Binoc.Core.Model;

// Category payloads. These are deliberately skeletal at M0 — just enough shape to anchor the model and
// give M1+ analysers a typed slot to fill. Fields will be added milestone by milestone; nothing outside
// Binoc.Core depends on their internals yet.

/// <summary>
/// Identity: who this app claims to be. Populated at M1 from the Android manifest (APK/AAB) or the iOS
/// <c>Info.plist</c>. Every timestamp carries a provenance label rather than being asserted as fact
/// (findings §design-decisions, decision D4).
/// </summary>
public sealed class IdentityInfo
{
    /// <summary>Android package name or iOS bundle identifier.</summary>
    public string? PackageId { get; set; }

    /// <summary>Marketing / display version (Android <c>versionName</c>, iOS <c>CFBundleShortVersionString</c>).</summary>
    public string? VersionName { get; set; }

    /// <summary>Build number (Android <c>versionCode</c>, iOS <c>CFBundleVersion</c>).</summary>
    public string? VersionCode { get; set; }

    // ── Android ──
    /// <summary>Minimum supported SDK level (Android <c>minSdkVersion</c>).</summary>
    public string? MinSdk { get; set; }

    /// <summary>Target SDK level (Android <c>targetSdkVersion</c>).</summary>
    public string? TargetSdk { get; set; }

    /// <summary>Compile SDK level, when present (Android <c>compileSdkVersion</c>).</summary>
    public string? CompileSdk { get; set; }

    /// <summary>Whether the app is marked debuggable — a release build should be false/absent.</summary>
    public bool? IsDebuggable { get; set; }

    // ── iOS ──
    /// <summary>Minimum OS version (iOS <c>MinimumOSVersion</c>).</summary>
    public string? MinimumOsVersion { get; set; }

    /// <summary>Supported device family (iOS <c>UIDeviceFamily</c>: iPhone/iPad).</summary>
    public string? DeviceFamily { get; set; }

    /// <summary>Build provenance strings (iOS <c>DTSDKName</c>/<c>DTXcode</c>, Android build fingerprint) — a
    /// weak proxy for build time (decision D4).</summary>
    public string? BuildProvenance { get; set; }
}

/// <summary>
/// Signing: who signed the binary and whether that signature can be trusted. Populated at M2. For AAB this
/// must state explicitly that the signer is usually the <em>upload</em> key, not the distribution key
/// (findings §AAB, decision D4).
/// </summary>
public sealed class SigningInfo
{
    /// <summary>Human-readable summary of the signing state (placeholder until M2).</summary>
    public string? Summary { get; set; }
}
