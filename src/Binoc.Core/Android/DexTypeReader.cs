using System.Buffers.Binary;

namespace Binoc.Core.Android;

/// <summary>
/// Reads a <c>.dex</c> file's <em>defined-class</em> type descriptors and counts how many carry a
/// machine-renamed simple name (e.g. <c>a</c>, <c>b</c>, <c>ab</c>) — the core signal that a shrinker/obfuscator
/// (R8, ProGuard, DexGuard) has run. Unlike <see cref="DexReader"/> (header only), this walks three DEX tables,
/// so it needs the whole decompressed DEX; callers bound how large a DEX they hand it. Pure managed (decision D1).
///
/// <para>DEX header offsets used (little-endian): string_ids_size@56/off@60, type_ids_size@64/off@68,
/// class_defs_size@96/off@100. A <c>class_def_item</c> is 32 bytes; its first u4 is the type index of the class.
/// A <c>type_id_item</c> is a u4 index into string_ids; a <c>string_id_item</c> is a u4 offset to a
/// <c>string_data_item</c> (uleb128 length, then modified-UTF-8 bytes, then a NUL).</para>
/// </summary>
public static class DexTypeReader
{
    private static readonly byte[] Magic = { 0x64, 0x65, 0x78, 0x0a }; // "dex\n"

    // Defensive ceiling so a crafted header can't make us scan forever.
    private const int MaxClassDefs = 500_000;

    /// <summary>Defined-class name statistics for one DEX.</summary>
    /// <param name="DefinedClasses">Classes defined in this DEX whose descriptor resolved.</param>
    /// <param name="MangledClasses">Of those, how many have a machine-renamed simple name.</param>
    public readonly record struct NameStats(int DefinedClasses, int MangledClasses);

    /// <summary>Reads the defined classes and returns name stats, or null if the DEX is too short/malformed.</summary>
    public static NameStats? Read(byte[] dex)
    {
        if (dex.Length < 112 || !dex.AsSpan(0, 4).SequenceEqual(Magic)) return null;

        uint stringIdsSize = U32(dex, 56), stringIdsOff = U32(dex, 60);
        uint typeIdsSize = U32(dex, 64), typeIdsOff = U32(dex, 68);
        uint classDefsSize = U32(dex, 96), classDefsOff = U32(dex, 100);

        // Every table must lie inside the buffer, or we can't trust any of it.
        if (!Fits(dex, stringIdsOff, stringIdsSize, 4)) return null;
        if (!Fits(dex, typeIdsOff, typeIdsSize, 4)) return null;
        if (!Fits(dex, classDefsOff, classDefsSize, 32)) return null;

        int count = (int)Math.Min(classDefsSize, MaxClassDefs);
        int defined = 0, mangled = 0;

        for (int i = 0; i < count; i++)
        {
            uint classIdx = U32(dex, (int)classDefsOff + i * 32);
            if (classIdx >= typeIdsSize) continue;

            uint descIdx = U32(dex, (int)typeIdsOff + (int)classIdx * 4);
            if (descIdx >= stringIdsSize) continue;

            uint strOff = U32(dex, (int)stringIdsOff + (int)descIdx * 4);
            var descriptor = ReadStringData(dex, strOff);
            if (descriptor is null) continue;

            defined++;
            if (IsMangledClassDescriptor(descriptor)) mangled++;
        }

        return new NameStats(defined, mangled);
    }

    /// <summary>True when a class type descriptor (<c>Lpkg/Name;</c>) has a machine-renamed simple name —
    /// a short, all-lowercase-leading token from an obfuscation dictionary (<c>a</c>, <c>b</c>, …, <c>zz</c>).</summary>
    public static bool IsMangledClassDescriptor(string descriptor)
    {
        // Only object types: "L....;".
        if (descriptor.Length < 3 || descriptor[0] != 'L' || descriptor[^1] != ';') return false;
        string inner = descriptor[1..^1];               // e.g. "com/a/b$c"

        int slash = inner.LastIndexOf('/');
        string simple = slash >= 0 ? inner[(slash + 1)..] : inner;

        // For nested classes take the innermost token; the outer may itself be mangled.
        int dollar = simple.LastIndexOf('$');
        if (dollar >= 0) simple = simple[(dollar + 1)..];

        if (simple.Length is 0 or > 2) return false;     // ≤2 chars is the conservative, low-false-positive window
        if (simple[0] is < 'a' or > 'z') return false;   // dictionary names start lowercase (excludes R, IO, UI, …)
        foreach (char c in simple)
            if (c is (< 'a' or > 'z') and (< '0' or > '9')) return false;
        return true;
    }

    private static uint U32(byte[] d, int off) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off, 4));

    // Does a table of `elemCount` items of `elemSize` bytes starting at `off` fit within the buffer?
    private static bool Fits(byte[] d, uint off, uint elemCount, int elemSize)
    {
        long end = (long)off + (long)elemCount * elemSize;
        return off <= d.Length && end <= d.Length;
    }

    // Reads a string_data_item: uleb128 utf16 length (skipped), then MUTF-8 bytes up to a NUL. ASCII-only
    // decode is fine — DEX type descriptors are ASCII. Returns null on any out-of-bounds read.
    private static string? ReadStringData(byte[] d, uint off)
    {
        int pos = (int)off;
        if (off >= d.Length) return null;

        // Skip the uleb128 length prefix.
        while (pos < d.Length && (d[pos] & 0x80) != 0) pos++;
        pos++; // final length byte
        if (pos > d.Length) return null;

        int start = pos;
        while (pos < d.Length && d[pos] != 0) pos++;
        if (pos >= d.Length) return null; // unterminated

        return System.Text.Encoding.ASCII.GetString(d, start, pos - start);
    }
}
