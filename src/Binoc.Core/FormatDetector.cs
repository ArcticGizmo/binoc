using System.IO.Compression;
using Binoc.Core.Model;

namespace Binoc.Core;

/// <summary>
/// Classifies a file as APK, AAB or IPA by what it actually contains, not by its extension (findings §5:
/// all three are ZIPs; the difference is the encoding/layout inside). We first confirm the ZIP local-file
/// magic (<c>PK\x03\x04</c>), then look for the entries that distinguish the three formats. The extension
/// is used only as a last-resort tiebreaker.
/// </summary>
public static class FormatDetector
{
    // ZIP local file header magic. (Empty archives use PK\x05\x06 and spanned ones PK\x07\x08, but a real
    // app binary always has entries, so the local-file magic is the one we require.)
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    /// <summary>Detect the format of the file at <paramref name="path"/>. Returns
    /// <see cref="BinaryFormat.Unknown"/> for a non-ZIP file or a ZIP whose contents match none of the
    /// three formats. Never throws for a missing/unreadable file — returns <see cref="BinaryFormat.Unknown"/>.</summary>
    public static BinaryFormat Detect(string path)
    {
        try
        {
            if (!HasZipMagic(path)) return BinaryFormat.Unknown;
            using var zip = ZipFile.OpenRead(path);
            return DetectFromEntries(zip, path);
        }
        catch
        {
            return BinaryFormat.Unknown;
        }
    }

    /// <summary>The core rule, split out so it can be unit-tested against an in-memory
    /// <see cref="ZipArchive"/> without touching disk.</summary>
    public static BinaryFormat DetectFromEntries(ZipArchive zip, string? path = null)
    {
        bool hasBundleConfig = false, hasBaseManifest = false;
        bool hasRootManifest = false, hasDex = false;
        bool hasPayloadApp = false;

        foreach (var entry in zip.Entries)
        {
            // Normalise separators; ZIP always uses '/', but be defensive.
            var name = entry.FullName.Replace('\\', '/');

            if (name == "BundleConfig.pb") hasBundleConfig = true;
            else if (name == "base/manifest/AndroidManifest.xml") hasBaseManifest = true;
            else if (name == "AndroidManifest.xml") hasRootManifest = true;
            else if (name.StartsWith("classes", StringComparison.Ordinal) && name.EndsWith(".dex", StringComparison.Ordinal)
                     && !name.Contains('/')) hasDex = true;
            else if (name.StartsWith("Payload/", StringComparison.Ordinal) && name.Contains(".app/")) hasPayloadApp = true;
        }

        // AAB is the most specific (protobuf publishing layout) — check it before APK, since a stray
        // root AndroidManifest.xml shouldn't win over the bundle markers.
        if (hasBundleConfig || hasBaseManifest) return BinaryFormat.Aab;
        if (hasRootManifest && hasDex) return BinaryFormat.Apk;
        if (hasPayloadApp) return BinaryFormat.Ipa;

        // Partial matches: a root manifest alone still strongly implies an APK.
        if (hasRootManifest) return BinaryFormat.Apk;

        // Last resort: fall back to the extension only when the contents were inconclusive.
        return FromExtension(path);
    }

    private static BinaryFormat FromExtension(string? path)
    {
        if (path is null) return BinaryFormat.Unknown;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".apk" => BinaryFormat.Apk,
            ".aab" => BinaryFormat.Aab,
            ".ipa" => BinaryFormat.Ipa,
            _ => BinaryFormat.Unknown,
        };
    }

    private static bool HasZipMagic(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        return fs.Read(head) == 4 && head.SequenceEqual(ZipMagic);
    }
}
