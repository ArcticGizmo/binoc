using System.Buffers.Binary;
using System.Text;
using Binoc.Core.Ios;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// The Mach-O reader, driven by a hand-built thin 64-bit arm64 image with a load-dylib, an
/// encryption-info command (cryptid set) and a code-signature command. No toolchain needed.
/// </summary>
public class MachOReaderTests
{
    private static byte[] BuildThinArm64(bool pie, uint cryptid, string dylib)
    {
        // Load commands.
        var dylibName = Encoding.ASCII.GetBytes(dylib + "\0");
        int dylibCmdSize = Align8(24 + dylibName.Length);
        var lcDylib = new byte[dylibCmdSize];
        BinaryPrimitives.WriteUInt32LittleEndian(lcDylib.AsSpan(0), 0xC);          // LC_LOAD_DYLIB
        BinaryPrimitives.WriteUInt32LittleEndian(lcDylib.AsSpan(4), (uint)dylibCmdSize);
        BinaryPrimitives.WriteUInt32LittleEndian(lcDylib.AsSpan(8), 24);           // name offset
        dylibName.CopyTo(lcDylib, 24);

        var lcEnc = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(lcEnc.AsSpan(0), 0x2C);           // LC_ENCRYPTION_INFO_64
        BinaryPrimitives.WriteUInt32LittleEndian(lcEnc.AsSpan(4), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(lcEnc.AsSpan(16), cryptid);       // cryptid

        var lcSig = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(lcSig.AsSpan(0), 0x1D);           // LC_CODE_SIGNATURE
        BinaryPrimitives.WriteUInt32LittleEndian(lcSig.AsSpan(4), 16);

        var cmds = Concat(lcDylib, lcEnc, lcSig);
        var buf = new byte[32 + cmds.Length];

        // mach_header_64.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0), 0xFEEDFACF);       // magic (64-bit)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), 0x0100000C);       // cputype ARM64
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), 0);                // cpusubtype (arm64)
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), 2);               // filetype MH_EXECUTE
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16), 3);               // ncmds
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20), (uint)cmds.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24), pie ? 0x200000u : 0u); // flags (MH_PIE)
        cmds.CopyTo(buf, 32);
        return buf;
    }

    private static int Align8(int n) => (n + 7) & ~7;
    private static byte[] Concat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }

    [Fact]
    public void Reads_a_thin_encrypted_pie_arm64_binary()
    {
        var info = MachOReader.Read(BuildThinArm64(pie: true, cryptid: 1, dylib: "/usr/lib/libTest.dylib"));

        Assert.NotNull(info);
        Assert.False(info!.IsFat);
        var a = Assert.Single(info.Architectures);
        Assert.Equal("arm64", a.Arch);
        Assert.True(a.Is64Bit);
        Assert.True(a.Pie);
        Assert.True(a.Encrypted);
        Assert.True(a.HasCodeSignature);
        Assert.Contains("/usr/lib/libTest.dylib", a.Dylibs);
    }

    [Fact]
    public void Non_pie_unencrypted_reads_accordingly()
    {
        var info = MachOReader.Read(BuildThinArm64(pie: false, cryptid: 0, dylib: "/usr/lib/libc.dylib"));
        var a = Assert.Single(info!.Architectures);
        Assert.False(a.Pie);
        Assert.False(a.Encrypted);
    }

    [Fact]
    public void Non_macho_returns_null() => Assert.Null(MachOReader.Read(Encoding.ASCII.GetBytes("this is not macho")));
}
