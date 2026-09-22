using System.Buffers.Binary;

namespace Binoc.Core.Android;

/// <summary>
/// Reads the security-relevant facts from an ELF shared object (<c>lib/&lt;abi&gt;/*.so</c>) — the "checksec"
/// a native library gets: architecture, NX (non-executable stack), RELRO (none/partial/full), stack-canary
/// presence, and whether it's stripped of debug symbols. Pure managed (decision D1); parses the ELF/program/
/// section/dynamic headers directly. Little-endian (every Android ABI is LE); returns null for non-ELF or
/// big-endian input.
/// </summary>
public static class ElfReader
{
    public sealed record ElfInfo(bool Is64Bit, string Arch, bool Nx, string Relro, bool StackCanary, bool Stripped, bool IsPie);

    // Program-header types.
    private const uint PtDynamic = 2;
    private const uint PtGnuStack = 0x6474e551;
    private const uint PtGnuRelro = 0x6474e552;
    // Section-header types.
    private const uint ShtSymtab = 2;
    // Dynamic tags.
    private const long DtBindNow = 24;
    private const long DtFlags = 30;
    private const long DtFlags1 = 0x6ffffffb;
    private const ulong DfBindNow = 0x8;
    private const ulong Df1Now = 0x1;
    private const uint PfX = 0x1;

    public static ElfInfo? Read(byte[] d)
    {
        if (d.Length < 64 || d[0] != 0x7f || d[1] != (byte)'E' || d[2] != (byte)'L' || d[3] != (byte)'F') return null;
        bool is64 = d[4] == 2;
        if (d[5] != 1) return null; // EI_DATA: only little-endian

        try
        {
            ushort eType = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(16));
            ushort eMachine = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(18));

            long phoff; ushort phentsize, phnum;
            long shoff; ushort shentsize, shnum;
            if (is64)
            {
                phoff = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(32));
                shoff = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(40));
                phentsize = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(54));
                phnum = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(56));
                shentsize = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(58));
                shnum = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(60));
            }
            else
            {
                phoff = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(28));
                shoff = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(32));
                phentsize = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(42));
                phnum = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(44));
                shentsize = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(46));
                shnum = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(48));
            }

            bool nx = true;          // modern default; only a PT_GNU_STACK with PF_X flips it off
            bool relroSeg = false;
            long dynOff = 0, dynSize = 0;

            for (int i = 0; i < phnum; i++)
            {
                long p = phoff + (long)i * phentsize;
                if (p + phentsize > d.Length) break;
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)p));
                uint flags; long off, filesz;
                if (is64)
                {
                    flags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)p + 4));
                    off = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)p + 8));
                    filesz = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)p + 32));
                }
                else
                {
                    off = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)p + 4));
                    filesz = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)p + 16));
                    flags = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)p + 24));
                }

                if (type == PtGnuStack) nx = (flags & PfX) == 0;
                else if (type == PtGnuRelro) relroSeg = true;
                else if (type == PtDynamic) { dynOff = off; dynSize = filesz; }
            }

            bool bindNow = ScanDynamicForBindNow(d, is64, dynOff, dynSize);
            string relro = !relroSeg ? "none" : bindNow ? "full" : "partial";

            bool stripped = !HasSymtab(d, is64, shoff, shentsize, shnum);
            bool canary = HasStackCanary(d, is64, shoff, shentsize, shnum);

            return new ElfInfo(is64, MachineName(eMachine, is64), nx, relro, canary, stripped, eType == 3 /*ET_DYN*/);
        }
        catch
        {
            return null;
        }
    }

    private static bool ScanDynamicForBindNow(byte[] d, bool is64, long off, long size)
    {
        if (off <= 0 || size <= 0 || off + size > d.Length) return false;
        int entSize = is64 ? 16 : 8;
        for (long e = off; e + entSize <= off + size; e += entSize)
        {
            long tag; ulong val;
            if (is64)
            {
                tag = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)e));
                val = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)e + 8));
            }
            else
            {
                tag = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan((int)e));
                val = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)e + 4));
            }
            if (tag == 0) break; // DT_NULL terminates
            if (tag == DtBindNow) return true;
            if (tag == DtFlags && (val & DfBindNow) != 0) return true;
            if (tag == DtFlags1 && (val & Df1Now) != 0) return true;
        }
        return false;
    }

    private static bool HasSymtab(byte[] d, bool is64, long shoff, ushort shentsize, ushort shnum)
    {
        if (shoff <= 0 || shnum == 0) return false;
        for (int i = 0; i < shnum; i++)
        {
            long s = shoff + (long)i * shentsize;
            if (s + 8 > d.Length) break;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)s + 4));
            if (type == ShtSymtab) return true;
        }
        return false;
    }

    // Canary presence via the .dynstr string table: modern toolchains emit __stack_chk_fail as an imported
    // symbol when stack protection is on. Scanning .dynstr avoids resolving symbol vaddrs.
    private static bool HasStackCanary(byte[] d, bool is64, long shoff, ushort shentsize, ushort shnum)
    {
        if (shoff <= 0 || shnum == 0) return false;
        for (int i = 0; i < shnum; i++)
        {
            long s = shoff + (long)i * shentsize;
            if (s + shentsize > d.Length) break;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)s + 4));
            if (type != 3) continue; // SHT_STRTAB
            long off, size;
            if (is64)
            {
                off = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)s + 24));
                size = (long)BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan((int)s + 32));
            }
            else
            {
                off = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)s + 16));
                size = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)s + 20));
            }
            if (off <= 0 || size <= 0 || off + size > d.Length) continue;
            if (ContainsAscii(d, (int)off, (int)size, "__stack_chk_fail")) return true;
        }
        return false;
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

    private static string MachineName(ushort machine, bool is64) => machine switch
    {
        3 => "x86",
        62 => "x86-64",
        40 => "ARM (32-bit)",
        183 => "AArch64",
        _ => $"machine 0x{machine:x}{(is64 ? " (64-bit)" : " (32-bit)")}",
    };
}
