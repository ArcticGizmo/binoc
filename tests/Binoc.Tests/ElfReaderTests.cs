using System.Buffers.Binary;
using System.Text;
using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// ELF checksec, exercised with hand-built 64-bit little-endian ELF images (there's no cross-compiler to
/// hand). The builder lays out a header + three program headers (GNU_STACK, optional GNU_RELRO, DYNAMIC),
/// a dynamic section, and section headers (optional SYMTAB, a STRTAB whose bytes optionally contain the
/// canary symbol) — enough to drive every branch of the reader.
/// </summary>
public class ElfReaderTests
{
    private static byte[] BuildElf(bool execStack, bool relroSeg, bool bindNow, bool canarySymbol, bool includeSymtab)
    {
        const int ehdr = 64, phEnt = 56, shEnt = 64, phNum = 3, shNum = 3;
        int phoff = ehdr;
        int shoff = phoff + phEnt * phNum;
        int dynOff = shoff + shEnt * shNum;
        int dynSize = 32; // two 16-byte entries
        int strOff = dynOff + dynSize;
        var strBytes = Encoding.ASCII.GetBytes(canarySymbol ? "__stack_chk_fail\0" : "nothing\0");
        int total = strOff + strBytes.Length;

        var d = new byte[total];

        // ELF header.
        d[0] = 0x7f; d[1] = (byte)'E'; d[2] = (byte)'L'; d[3] = (byte)'F';
        d[4] = 2; // 64-bit
        d[5] = 1; // little-endian
        d[6] = 1; // version
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(16), 3);   // e_type = ET_DYN
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(18), 183); // AArch64
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(32), (ulong)phoff);
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(40), (ulong)shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(54), phEnt);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(56), phNum);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(58), shEnt);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(60), shNum);

        // Program headers.
        void Ph(int i, uint type, uint flags, long off, long filesz)
        {
            int p = phoff + i * phEnt;
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(p), type);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(p + 4), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(p + 8), (ulong)off);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(p + 32), (ulong)filesz);
        }
        Ph(0, 0x6474e551, execStack ? 7u : 6u, 0, 0);              // PT_GNU_STACK (RWX vs RW)
        Ph(1, relroSeg ? 0x6474e552u : 0x60000000u, 4, 0, 0);       // PT_GNU_RELRO (or a benign OS-type)
        Ph(2, 2, 6, dynOff, dynSize);                               // PT_DYNAMIC

        // Dynamic section: DT_FLAGS (30) = DF_BIND_NOW (0x8) when requested, then DT_NULL.
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(dynOff), 30);
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(dynOff + 8), bindNow ? 0x8u : 0u);
        BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(dynOff + 16), 0); // DT_NULL

        // Section headers.
        void Sh(int i, uint type, long off, long size)
        {
            int s = shoff + i * shEnt;
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(s + 4), type);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(s + 24), (ulong)off);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(s + 32), (ulong)size);
        }
        Sh(0, 0, 0, 0);                                            // null section
        Sh(1, includeSymtab ? 2u : 1u, 0, 0);                      // SHT_SYMTAB or a plain PROGBITS
        Sh(2, 3, strOff, strBytes.Length);                         // SHT_STRTAB (.dynstr)

        strBytes.CopyTo(d, strOff);
        return d;
    }

    [Fact]
    public void Hardened_library_reads_as_secure()
    {
        var info = ElfReader.Read(BuildElf(execStack: false, relroSeg: true, bindNow: true, canarySymbol: true, includeSymtab: false));
        Assert.NotNull(info);
        Assert.True(info!.Is64Bit);
        Assert.Equal("AArch64", info.Arch);
        Assert.True(info.Nx);
        Assert.Equal("full", info.Relro);
        Assert.True(info.StackCanary);
        Assert.True(info.Stripped);   // no SYMTAB
        Assert.True(info.IsPie);
    }

    [Fact]
    public void Weak_library_reads_as_insecure()
    {
        var info = ElfReader.Read(BuildElf(execStack: true, relroSeg: false, bindNow: false, canarySymbol: false, includeSymtab: true));
        Assert.NotNull(info);
        Assert.False(info!.Nx);
        Assert.Equal("none", info.Relro);
        Assert.False(info.StackCanary);
        Assert.False(info.Stripped); // SYMTAB present
    }

    [Fact]
    public void Partial_relro_without_bind_now()
    {
        var info = ElfReader.Read(BuildElf(execStack: false, relroSeg: true, bindNow: false, canarySymbol: false, includeSymtab: false));
        Assert.Equal("partial", info!.Relro);
    }

    [Fact]
    public void Non_elf_returns_null() => Assert.Null(ElfReader.Read(Encoding.ASCII.GetBytes("not an elf file at all")));
}
