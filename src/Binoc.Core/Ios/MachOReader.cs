using System.Buffers.Binary;

namespace Binoc.Core.Ios;

/// <summary>
/// Reads an iOS Mach-O executable: its architecture slices (thin or fat/universal), and per-slice the
/// security-relevant facts — PIE, FairPlay encryption (<c>cryptid</c>), code-signature presence, a stack
/// canary (symbol-table scan), and the linked dylibs/frameworks. Pure managed (decision D1).
/// </summary>
public static class MachOReader
{
    private const uint FatMagic = 0xCAFEBABE;   // big-endian on disk
    private const uint FatMagic64 = 0xCAFEBABF;
    private const uint MachO32 = 0xFEEDFACE;    // little-endian slices (iOS)
    private const uint MachO64 = 0xFEEDFACF;

    private const uint MhPie = 0x200000;

    // Load commands.
    private const uint LcSymtab = 0x2;
    private const uint LcLoadDylib = 0xC;
    private const uint LcLoadWeakDylib = 0x80000018;
    private const uint LcCodeSignature = 0x1D;
    private const uint LcEncryptionInfo = 0x21;
    private const uint LcEncryptionInfo64 = 0x2C;

    public sealed record ArchInfo(
        string Arch, bool Is64Bit, bool Pie, bool Encrypted, bool HasCodeSignature, bool StackCanary,
        IReadOnlyList<string> Dylibs);

    public sealed record Result(bool IsFat, IReadOnlyList<ArchInfo> Architectures);

    public static Result? Read(byte[] d)
    {
        if (d.Length < 8) return null;
        uint beMagic = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0));

        var arches = new List<ArchInfo>();
        if (beMagic is FatMagic or FatMagic64)
        {
            bool fat64 = beMagic == FatMagic64;
            int nfat = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
            int p = 8;
            int entrySize = fat64 ? 32 : 20;
            for (int i = 0; i < nfat && p + entrySize <= d.Length; i++, p += entrySize)
            {
                long offset = fat64
                    ? (long)BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(p + 8))
                    : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 8));
                if (offset > 0 && offset < d.Length && ParseThin(d, (int)offset) is { } a) arches.Add(a);
            }
            return new Result(true, arches);
        }

        if (ParseThin(d, 0) is { } thin) { arches.Add(thin); return new Result(false, arches); }
        return null;
    }

    private static ArchInfo? ParseThin(byte[] d, int baseOff)
    {
        if (baseOff + 28 > d.Length) return null;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(baseOff));
        bool is64 = magic == MachO64;
        if (magic != MachO32 && magic != MachO64) return null;

        uint cpuType = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(baseOff + 4));
        uint cpuSub = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(baseOff + 8));
        uint ncmds = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(baseOff + 16));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(baseOff + 24));
        int lcStart = baseOff + (is64 ? 32 : 28);

        bool pie = (flags & MhPie) != 0;
        bool encrypted = false, codeSig = false, canary = false;
        var dylibs = new List<string>();

        int p = lcStart;
        for (uint i = 0; i < ncmds; i++)
        {
            if (p + 8 > d.Length) break;
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
            uint cmdSize = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4));
            if (cmdSize < 8 || p + cmdSize > d.Length) break;

            switch (cmd)
            {
                case LcEncryptionInfo or LcEncryptionInfo64:
                    // cmd, cmdsize, cryptoff, cryptsize, cryptid
                    if (p + 20 <= d.Length && BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 16)) != 0)
                        encrypted = true;
                    break;
                case LcCodeSignature:
                    codeSig = true;
                    break;
                case LcLoadDylib or LcLoadWeakDylib:
                {
                    // cmd, cmdsize, name offset (from cmd start), timestamp, cur ver, compat ver
                    uint nameOff = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 8));
                    if (nameOff < cmdSize)
                        dylibs.Add(ReadCString(d, p + (int)nameOff, p + (int)cmdSize));
                    break;
                }
                case LcSymtab:
                {
                    // cmd, cmdsize, symoff, nsyms, stroff, strsize
                    int stroff = baseOff + (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 16));
                    int strsize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 20));
                    if (stroff > 0 && strsize > 0 && stroff + strsize <= d.Length)
                        canary = ContainsAscii(d, stroff, strsize, "__stack_chk_fail")
                              || ContainsAscii(d, stroff, strsize, "___stack_chk_guard");
                    break;
                }
            }

            p += (int)cmdSize;
        }

        return new ArchInfo(ArchName(cpuType, cpuSub), is64, pie, encrypted, codeSig, canary, dylibs);
    }

    private static string ArchName(uint cpuType, uint cpuSub) => cpuType switch
    {
        0x0100000C => (cpuSub & 0xff) == 2 ? "arm64e" : "arm64",
        0x0000000C => "arm (32-bit)",
        0x01000007 => "x86_64",
        0x00000007 => "x86",
        _ => $"cpu 0x{cpuType:x}",
    };

    private static string ReadCString(byte[] d, int start, int limit)
    {
        int end = start;
        while (end < limit && end < d.Length && d[end] != 0) end++;
        return System.Text.Encoding.UTF8.GetString(d, start, end - start);
    }

    private static bool ContainsAscii(byte[] d, int start, int len, string needle)
    {
        var pat = System.Text.Encoding.ASCII.GetBytes(needle);
        int end = start + len - pat.Length;
        for (int i = start; i <= end; i++)
        {
            int j = 0;
            while (j < pat.Length && d[i + j] == pat[j]) j++;
            if (j == pat.Length) return true;
        }
        return false;
    }
}
