using System.Buffers.Binary;
using System.Text;
using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// zipalign detection. Real ZIPs are hand-built here with a single <em>stored</em> entry whose local
/// extra-field length is chosen to place the data at a known offset — so 4-byte alignment can be asserted
/// deterministically (System.IO.Compression gives no control over data offsets).
/// </summary>
public class ZipAlignmentTests
{
    // Writes a one-entry stored ZIP; the local extra field is `extraLen` zero bytes, so the entry's data
    // begins at offset 30 + name.Length + extraLen.
    private static string WriteStoredZip(string name, int extraLen, byte[] data)
    {
        var fn = Encoding.UTF8.GetBytes(name);
        var ms = new MemoryStream();

        // Local file header.
        long localOffset = ms.Position;
        WriteU32(ms, 0x04034b50);
        WriteU16(ms, 20); WriteU16(ms, 0); WriteU16(ms, 0);      // version, flags, method=stored
        WriteU16(ms, 0); WriteU16(ms, 0);                         // time, date
        WriteU32(ms, 0);                                          // crc (probe ignores)
        WriteU32(ms, (uint)data.Length); WriteU32(ms, (uint)data.Length);
        WriteU16(ms, (ushort)fn.Length); WriteU16(ms, (ushort)extraLen);
        ms.Write(fn); ms.Write(new byte[extraLen]); ms.Write(data);

        // Central directory.
        long cdOffset = ms.Position;
        WriteU32(ms, 0x02014b50);
        WriteU16(ms, 20); WriteU16(ms, 20); WriteU16(ms, 0); WriteU16(ms, 0);
        WriteU16(ms, 0); WriteU16(ms, 0);
        WriteU32(ms, 0); WriteU32(ms, (uint)data.Length); WriteU32(ms, (uint)data.Length);
        WriteU16(ms, (ushort)fn.Length); WriteU16(ms, 0); WriteU16(ms, 0); // fn, extra, comment
        WriteU16(ms, 0); WriteU16(ms, 0); WriteU32(ms, 0);                 // disk, iattr, eattr
        WriteU32(ms, (uint)localOffset);
        ms.Write(fn);
        long cdSize = ms.Position - cdOffset;

        // EOCD.
        WriteU32(ms, 0x06054b50);
        WriteU16(ms, 0); WriteU16(ms, 0); WriteU16(ms, 1); WriteU16(ms, 1);
        WriteU32(ms, (uint)cdSize); WriteU32(ms, (uint)cdOffset); WriteU16(ms, 0);

        var path = Path.Combine(Path.GetTempPath(), $"binoc-align-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [Fact]
    public void Detects_a_misaligned_stored_entry()
    {
        // name "a.txt" (5): dataOffset = 30 + 5 + 0 = 35, 35 % 4 == 3 → misaligned.
        var path = WriteStoredZip("a.txt", extraLen: 0, data: Encoding.ASCII.GetBytes("hello"));
        try
        {
            var info = ZipAlignment.Probe(path);
            Assert.NotNull(info);
            Assert.False(info!.Aligned4);
            Assert.Equal(1, info.MisalignedCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Detects_a_4byte_aligned_stored_entry()
    {
        // name "a.txt" (5): 30 + 5 + extra ≡ 0 (mod 4) needs extra ≡ 1 (mod 4); extra = 1 → dataOffset 36.
        var path = WriteStoredZip("a.txt", extraLen: 1, data: Encoding.ASCII.GetBytes("hello"));
        try
        {
            var info = ZipAlignment.Probe(path);
            Assert.NotNull(info);
            Assert.True(info!.Aligned4);
            Assert.Equal(0, info.MisalignedCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Flags_native_libs_not_16k_aligned()
    {
        // A stored .so at a small offset can't be 16 KB-aligned.
        var path = WriteStoredZip("lib/arm64-v8a/x.so", extraLen: 0, data: new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });
        try
        {
            var info = ZipAlignment.Probe(path);
            Assert.NotNull(info);
            Assert.False(info!.NativeLibs16k);
        }
        finally { File.Delete(path); }
    }

    private static void WriteU16(Stream s, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); s.Write(b); }
    private static void WriteU32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); s.Write(b); }
}
