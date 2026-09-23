namespace Binoc.Core.Model;

/// <summary>
/// The three mobile-binary formats binoc analyses, plus <see cref="Unknown"/> for anything we can't
/// confidently classify. All three are ZIP archives (see <see cref="FormatDetector"/>); the distinction
/// is what's <em>inside</em> the archive, not the extension.
/// </summary>
public enum BinaryFormat
{
    Unknown = 0,

    /// <summary>Android application package — installable ZIP with a binary <c>AndroidManifest.xml</c> at
    /// the root and <c>classes*.dex</c>.</summary>
    Apk,

    /// <summary>Android App Bundle — a <em>publishing</em> ZIP (not installable) with protobuf manifests,
    /// per-module directories and a <c>BundleConfig.pb</c>.</summary>
    Aab,

    /// <summary>iOS application archive — ZIP containing <c>Payload/&lt;App&gt;.app/</c>.</summary>
    Ipa,
}
