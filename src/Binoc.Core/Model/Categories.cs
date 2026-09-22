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
