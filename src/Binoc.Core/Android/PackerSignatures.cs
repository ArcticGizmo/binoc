namespace Binoc.Core.Android;

/// <summary>
/// A curated, offline signature set for the commercial Android packers/protectors that wrap an app's real DEX
/// (encrypting it and unpacking at runtime). These are the highest-confidence obfuscation signal: they're
/// identified by the loader's native library and by marker files the packer drops into <c>assets/</c> or the
/// archive root. A managed reimplementation of the APKiD-style idea (findings §Obfuscation) — no shelling out,
/// no third-party dependency (decision D1). Matching is by basename/marker, case-insensitive.
/// </summary>
public static class PackerSignatures
{
    /// <summary>A matched packer: the product name and the archive entry that gave it away.</summary>
    public readonly record struct Match(string Product, string Evidence);

    // Native loader libraries, keyed by lowercase basename prefix → product. A shipped .so whose file name
    // starts with one of these is the packer's runtime.
    private static readonly (string Prefix, string Product)[] NativeLibs =
    {
        ("libjiagu", "360 Jiagu"),
        ("libjgdtc", "360 Jiagu"),
        ("libprotectclass", "Bangcle"),
        ("libsecexe", "Bangcle"),
        ("libsecmain", "Bangcle"),
        ("libdexhelper", "SecNeo"),
        ("libshella", "Tencent Legu"),
        ("libshellx", "Tencent Legu"),
        ("libtup", "Tencent Legu"),
        ("libtosprotection", "Tencent Legu"),
        ("libmobisec", "Alibaba"),
        ("libnesec", "NetEase"),
        ("libapptoolkit", "NetEase"),
        ("libnqshield", "NQ Shield"),
        ("libnsecure", "Naga"),
        ("libapkprotect", "APKProtect"),
        ("libkwscmm", "Kiwi Security"),
        ("libkwsgmain", "Kiwi Security"),
        ("libkwslinker", "Kiwi Security"),
        ("libddog", "DexProtector"),
        ("libfdog", "DexProtector"),
        ("libx3g", "DexProtector"),
        ("libapsp", "Baidu"),
        ("libbaiduprotect", "Baidu"),
        ("libegis", "Payegis / Jiangu"),
        ("libapp-protection", "Promon SHIELD"),
    };

    // Marker files (matched by suffix on the normalised '/'-path) a packer drops into the archive.
    private static readonly (string Suffix, string Product)[] MarkerFiles =
    {
        ("assets/libjiagu.so", "360 Jiagu"),
        ("assets/libjiagu_x86.so", "360 Jiagu"),
        ("assets/libjiagu_a64.so", "360 Jiagu"),
        ("assets/baiduprotect.jar", "Baidu"),
        ("assets/baiduprotect1.jar", "Baidu"),
        ("assets/bangcleplugin", "Bangcle"),
        ("assets/bangcle_classes.jar", "Bangcle"),
        ("assets/secdata0.jar", "Bangcle"),
        ("assets/classes0.jar", "Tencent Legu"),
        ("assets/0oo00l111l1l", "Ijiami"),
        ("assets/ijiami.ajm", "Ijiami"),
        ("assets/ijiami.dat", "Ijiami"),
        ("assets/meta-data/manifest.mf", "Naga"),
        ("assets/dexprotector.txt", "DexProtector"),
        ("assets/8963", "Qihoo"),
    };

    /// <summary>Returns the packers whose signatures match any of the given normalised ('/'-separated) archive
    /// entry paths. Deduplicated by product, in first-seen order.</summary>
    public static IReadOnlyList<Match> Find(IEnumerable<string> normalisedPaths)
    {
        var matches = new List<Match>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in normalisedPaths)
        {
            var lower = path.ToLowerInvariant();
            var name = lower.Contains('/') ? lower[(lower.LastIndexOf('/') + 1)..] : lower;

            foreach (var (prefix, product) in NativeLibs)
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".so", StringComparison.Ordinal))
                    Add(product, path);

            foreach (var (suffix, product) in MarkerFiles)
                if (lower.EndsWith(suffix, StringComparison.Ordinal))
                    Add(product, path);
        }

        return matches;

        void Add(string product, string evidence)
        {
            if (seen.Add(product)) matches.Add(new Match(product, System.IO.Path.GetFileName(evidence)));
        }
    }
}
