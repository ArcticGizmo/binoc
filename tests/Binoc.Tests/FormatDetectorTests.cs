using System.IO.Compression;
using Binoc.Core;
using Binoc.Core.Model;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// Content-based format detection (M0). These build in-memory ZIPs with the entries that distinguish each
/// format, so they assert the real rule without needing sample binaries in the repo.
/// </summary>
public class FormatDetectorTests
{
    private static ZipArchive ZipWith(params string[] entryNames)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entryNames)
                zip.CreateEntry(name);
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    [Fact]
    public void Apk_is_detected_from_root_manifest_and_dex()
    {
        using var zip = ZipWith("AndroidManifest.xml", "classes.dex", "resources.arsc");
        Assert.Equal(BinaryFormat.Apk, FormatDetector.DetectFromEntries(zip, "x.apk"));
    }

    [Fact]
    public void Aab_is_detected_from_bundle_config()
    {
        using var zip = ZipWith("BundleConfig.pb", "base/manifest/AndroidManifest.xml", "base/dex/classes.dex");
        Assert.Equal(BinaryFormat.Aab, FormatDetector.DetectFromEntries(zip, "x.aab"));
    }

    [Fact]
    public void Aab_wins_over_a_stray_root_manifest()
    {
        // A bundle carrying a root manifest must still classify as AAB, not APK.
        using var zip = ZipWith("AndroidManifest.xml", "BundleConfig.pb");
        Assert.Equal(BinaryFormat.Aab, FormatDetector.DetectFromEntries(zip, "ambiguous.zip"));
    }

    [Fact]
    public void Ipa_is_detected_from_payload_app()
    {
        using var zip = ZipWith("Payload/MyApp.app/Info.plist", "Payload/MyApp.app/MyApp");
        Assert.Equal(BinaryFormat.Ipa, FormatDetector.DetectFromEntries(zip, "x.ipa"));
    }

    [Fact]
    public void Unknown_contents_fall_back_to_extension()
    {
        using var zip = ZipWith("readme.txt", "data/blob.bin");
        Assert.Equal(BinaryFormat.Ipa, FormatDetector.DetectFromEntries(zip, "mystery.ipa"));
    }

    [Fact]
    public void Unknown_contents_and_no_known_extension_is_unknown()
    {
        using var zip = ZipWith("readme.txt");
        Assert.Equal(BinaryFormat.Unknown, FormatDetector.DetectFromEntries(zip, "mystery.zip"));
    }

    [Fact]
    public void Missing_file_is_unknown_not_thrown()
    {
        Assert.Equal(BinaryFormat.Unknown, FormatDetector.Detect(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.apk")));
    }
}
