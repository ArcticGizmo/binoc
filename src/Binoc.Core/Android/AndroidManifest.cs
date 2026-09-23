using System.IO.Compression;
using Binoc.Core.Model;

namespace Binoc.Core.Android;

/// <summary>
/// Loads an Android manifest as a flat, document-order list of elements with string attributes, hiding the
/// APK-vs-AAB encoding difference: APK's binary <c>AndroidManifest.xml</c> (AXML) vs the AAB base module's
/// protobuf manifest. One shared shape for every Android reader (identity, posture, …).
/// </summary>
public static class AndroidManifest
{
    public readonly record struct Element(string Name, IReadOnlyDictionary<string, string> Attributes);

    /// <summary>Returns the manifest elements, or an empty list if none/undecodable.</summary>
    public static IReadOnlyList<Element> Load(ZipArchive archive, BinaryFormat format)
    {
        var (entry, isProto) = format switch
        {
            BinaryFormat.Apk => (archive.GetEntry("AndroidManifest.xml"), false),
            BinaryFormat.Aab => (archive.GetEntry("base/manifest/AndroidManifest.xml"), true),
            _ => (null, false),
        };
        if (entry is null) return Array.Empty<Element>();

        var bytes = ReadFully(entry);
        return isProto
            ? PbManifestReader.Parse(bytes).Select(e => new Element(e.Name, e.Attributes)).ToList()
            : AxmlReader.Parse(bytes).Select(e => new Element(e.Name, e.Attributes)).ToList();
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
