using System.Buffers.Binary;

namespace Binoc.Core.Android;

/// <summary>
/// Reads a <c>.dex</c> file's <em>defined</em> symbols — class type descriptors plus the app's own declared
/// methods and fields (via each class's <c>class_data</c>) — and counts how many carry a machine-renamed name
/// (e.g. <c>a</c>, <c>b</c>, <c>ab</c>), the core signal that a shrinker/obfuscator (R8, ProGuard, DexGuard) has
/// run. Only <em>defined</em> members are counted, so framework references (<c>toString</c>, <c>getString</c>, …)
/// don't dilute the ratio. Needs the whole decompressed DEX; callers bound how large a DEX they hand it. Pure
/// managed (decision D1).
///
/// <para>DEX header offsets (little-endian): string_ids@56/60, type_ids@64/68, field_ids@80/84, method_ids@88/92,
/// class_defs@96/100. A <c>class_def_item</c> is 32 bytes (class type index @0, class_data_off @24). A
/// <c>string_id_item</c> is a u4 offset to a <c>string_data_item</c> (uleb128 length, MUTF-8 bytes, NUL). A
/// <c>field_id_item</c>/<c>method_id_item</c> is 8 bytes with name_idx @4. <c>class_data_item</c> is a uleb128
/// stream (static/instance field counts, direct/virtual method counts, then delta-encoded members).</para>
/// </summary>
public static class DexTypeReader
{
    private static readonly byte[] Magic = { 0x64, 0x65, 0x78, 0x0a }; // "dex\n"

    // Defensive ceiling so a crafted header can't make us scan forever.
    private const int MaxClassDefs = 500_000;

    /// <summary>Defined-symbol name statistics for one DEX.</summary>
    /// <param name="DefinedClasses">Classes defined in this DEX whose descriptor resolved.</param>
    /// <param name="MangledClasses">Of those, how many have a machine-renamed simple name.</param>
    /// <param name="DefinedMethods">Methods declared by those classes (excluding &lt;init&gt;/&lt;clinit&gt;).</param>
    /// <param name="MangledMethods">Of those, how many have a machine-renamed name.</param>
    /// <param name="DefinedFields">Fields declared by those classes.</param>
    /// <param name="MangledFields">Of those, how many have a machine-renamed name.</param>
    /// <param name="TopPackage">The package (dotless, '/'-form) holding the most defined classes; empty = default
    /// package. A single dominant, very short package is the fingerprint of R8's <c>-repackageclasses</c>.</param>
    /// <param name="TopPackageCount">How many defined classes live in <see cref="TopPackage"/>.</param>
    public readonly record struct NameStats(
        int DefinedClasses, int MangledClasses,
        int DefinedMethods, int MangledMethods,
        int DefinedFields, int MangledFields,
        string TopPackage, int TopPackageCount)
    {
        /// <summary>All defined symbols (classes + methods + fields) considered for the ratio.</summary>
        public int DefinedSymbols => DefinedClasses + DefinedMethods + DefinedFields;

        /// <summary>How many of those carry a machine-renamed name.</summary>
        public int MangledSymbols => MangledClasses + MangledMethods + MangledFields;
    }

    /// <summary>Reads the defined symbols and returns name stats, or null if the DEX is too short/malformed.</summary>
    public static NameStats? Read(byte[] dex)
    {
        if (dex.Length < 112 || !dex.AsSpan(0, 4).SequenceEqual(Magic)) return null;

        uint stringIdsSize = U32(dex, 56), stringIdsOff = U32(dex, 60);
        uint typeIdsSize = U32(dex, 64), typeIdsOff = U32(dex, 68);
        uint fieldIdsSize = U32(dex, 80), fieldIdsOff = U32(dex, 84);
        uint methodIdsSize = U32(dex, 88), methodIdsOff = U32(dex, 92);
        uint classDefsSize = U32(dex, 96), classDefsOff = U32(dex, 100);

        // Every table we index into must lie inside the buffer, or we can't trust any of it.
        if (!Fits(dex, stringIdsOff, stringIdsSize, 4)) return null;
        if (!Fits(dex, typeIdsOff, typeIdsSize, 4)) return null;
        if (!Fits(dex, classDefsOff, classDefsSize, 32)) return null;
        bool haveFields = Fits(dex, fieldIdsOff, fieldIdsSize, 8);
        bool haveMethods = Fits(dex, methodIdsOff, methodIdsSize, 8);

        int count = (int)Math.Min(classDefsSize, MaxClassDefs);
        int classes = 0, mangledClasses = 0;
        int methods = 0, mangledMethods = 0;
        int fields = 0, mangledFields = 0;
        var packages = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            int def = (int)classDefsOff + i * 32;
            uint classIdx = U32(dex, def);
            if (classIdx >= typeIdsSize) continue;

            uint descIdx = U32(dex, (int)typeIdsOff + (int)classIdx * 4);
            if (descIdx >= stringIdsSize) continue;

            var descriptor = ResolveString(dex, stringIdsOff, descIdx);
            if (descriptor is null) continue;

            classes++;
            if (IsMangledClassDescriptor(descriptor)) mangledClasses++;

            var pkg = PackageOf(descriptor);
            packages[pkg] = (packages.TryGetValue(pkg, out var c) ? c : 0) + 1;

            // Declared members of this class (app-scoped) via class_data_off (@ +24).
            uint classDataOff = U32(dex, def + 24);
            if (classDataOff != 0 && classDataOff < dex.Length)
                CountMembers(dex, (int)classDataOff, stringIdsOff, stringIdsSize,
                    fieldIdsOff, fieldIdsSize, haveFields, methodIdsOff, methodIdsSize, haveMethods,
                    ref fields, ref mangledFields, ref methods, ref mangledMethods);
        }

        string topPackage = string.Empty;
        int topCount = 0;
        foreach (var kv in packages)
            if (kv.Value > topCount) { topCount = kv.Value; topPackage = kv.Key; }

        return new NameStats(classes, mangledClasses, methods, mangledMethods, fields, mangledFields, topPackage, topCount);
    }

    // Walks a class_data_item, resolving each declared field/method name and counting mangled ones.
    private static void CountMembers(
        byte[] d, int pos, uint stringIdsOff, uint stringIdsSize,
        uint fieldIdsOff, uint fieldIdsSize, bool haveFields,
        uint methodIdsOff, uint methodIdsSize, bool haveMethods,
        ref int fields, ref int mangledFields, ref int methods, ref int mangledMethods)
    {
        if (!TryUleb(d, ref pos, out uint staticFields)) return;
        if (!TryUleb(d, ref pos, out uint instanceFields)) return;
        if (!TryUleb(d, ref pos, out uint directMethods)) return;
        if (!TryUleb(d, ref pos, out uint virtualMethods)) return;

        // Two field lists (static, then instance); each is delta-encoded from index 0.
        for (int list = 0; list < 2; list++)
        {
            uint idx = 0;
            uint n = list == 0 ? staticFields : instanceFields;
            for (uint k = 0; k < n; k++)
            {
                if (!TryUleb(d, ref pos, out uint diff) || !TryUleb(d, ref pos, out _)) return; // idx_diff, access_flags
                idx += diff;
                if (!haveFields || idx >= fieldIdsSize) continue;
                var name = ResolveString(d, stringIdsOff, U32(d, (int)fieldIdsOff + (int)idx * 8 + 4));
                if (CountName(name, stringIdsSize)) { fields++; if (IsMangledMemberName(name!)) mangledFields++; }
            }
        }

        // Two method lists (direct, then virtual); each delta-encoded from index 0; three uleb fields each.
        for (int list = 0; list < 2; list++)
        {
            uint idx = 0;
            uint n = list == 0 ? directMethods : virtualMethods;
            for (uint k = 0; k < n; k++)
            {
                if (!TryUleb(d, ref pos, out uint diff) || !TryUleb(d, ref pos, out _) || !TryUleb(d, ref pos, out _)) return;
                idx += diff;
                if (!haveMethods || idx >= methodIdsSize) continue;
                var name = ResolveString(d, stringIdsOff, U32(d, (int)methodIdsOff + (int)idx * 8 + 4));
                if (CountName(name, stringIdsSize)) { methods++; if (IsMangledMemberName(name!)) mangledMethods++; }
            }
        }
    }

    // A member name counts toward the ratio unless it's null or a constructor (<init>/<clinit>, never renamed).
    private static bool CountName(string? name, uint stringIdsSize)
        => name is { Length: > 0 } && name[0] != '<';

    /// <summary>The package of a class descriptor (<c>Lpkg/sub/Name;</c> → <c>pkg/sub</c>); empty for the
    /// default package.</summary>
    public static string PackageOf(string descriptor)
    {
        if (descriptor.Length < 3 || descriptor[0] != 'L' || descriptor[^1] != ';') return string.Empty;
        string inner = descriptor[1..^1];
        int slash = inner.LastIndexOf('/');
        return slash >= 0 ? inner[..slash] : string.Empty;
    }

    /// <summary>True when a class type descriptor (<c>Lpkg/Name;</c>) has a machine-renamed simple name.</summary>
    public static bool IsMangledClassDescriptor(string descriptor)
    {
        if (descriptor.Length < 3 || descriptor[0] != 'L' || descriptor[^1] != ';') return false;
        string inner = descriptor[1..^1];               // e.g. "com/a/b$c"

        int slash = inner.LastIndexOf('/');
        string simple = slash >= 0 ? inner[(slash + 1)..] : inner;

        // For nested classes take the innermost token; the outer may itself be mangled.
        int dollar = simple.LastIndexOf('$');
        if (dollar >= 0) simple = simple[(dollar + 1)..];

        return IsMangledMemberName(simple);
    }

    /// <summary>True when a bare identifier looks like an obfuscation-dictionary name — short (≤2 chars),
    /// lowercase-leading, alphanumeric (<c>a</c>, <c>b</c>, …, <c>zz</c>, <c>a1</c>). Excludes <c>R</c>, <c>IO</c>, …</summary>
    public static bool IsMangledMemberName(string name)
    {
        if (name.Length is 0 or > 2) return false;       // ≤2 chars is the conservative, low-false-positive window
        if (name[0] is < 'a' or > 'z') return false;     // dictionary names start lowercase
        foreach (char c in name)
            if (c is (< 'a' or > 'z') and (< '0' or > '9')) return false;
        return true;
    }

    private static string? ResolveString(byte[] d, uint stringIdsOff, uint stringIdx)
    {
        uint strOff = U32(d, (int)stringIdsOff + (int)stringIdx * 4);
        return ReadStringData(d, strOff);
    }

    private static uint U32(byte[] d, int off) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off, 4));

    // Does a table of `elemCount` items of `elemSize` bytes starting at `off` fit within the buffer?
    private static bool Fits(byte[] d, uint off, uint elemCount, int elemSize)
    {
        long end = (long)off + (long)elemCount * elemSize;
        return off <= d.Length && end <= d.Length;
    }

    // Reads a uleb128; advances pos. Returns false on truncation/overflow.
    private static bool TryUleb(byte[] d, ref int pos, out uint value)
    {
        value = 0;
        int shift = 0;
        while (pos < d.Length)
        {
            byte b = d[pos++];
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
            if (shift >= 32) return false;
        }
        return false;
    }

    // Reads a string_data_item: uleb128 utf16 length (skipped), then MUTF-8 bytes up to a NUL. ASCII-only
    // decode is fine — DEX identifiers are ASCII. Returns null on any out-of-bounds read.
    private static string? ReadStringData(byte[] d, uint off)
    {
        int pos = (int)off;
        if (off >= d.Length) return null;

        while (pos < d.Length && (d[pos] & 0x80) != 0) pos++; // skip uleb128 length
        pos++;                                                // final length byte
        if (pos > d.Length) return null;

        int start = pos;
        while (pos < d.Length && d[pos] != 0) pos++;
        if (pos >= d.Length) return null; // unterminated

        return System.Text.Encoding.ASCII.GetString(d, start, pos - start);
    }
}
