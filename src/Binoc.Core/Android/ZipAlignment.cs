using System.Buffers.Binary;
using Binoc.Core.Model;

namespace Binoc.Core.Android;

/// <summary>
/// Probes an APK's zipalign status by parsing the ZIP structure directly — <c>System.IO.Compression</c>
/// doesn't expose an entry's on-disk data offset, which is exactly what alignment depends on. Reads the
/// End-of-Central-Directory record, walks the central directory, and for each <em>stored</em> (uncompressed)
/// entry reads its local header to find where the data begins, then checks 4-byte (and, for native libs,
/// 16 KB) alignment. Pure managed, seek-based so it never loads the whole APK. Returns null for a Zip64
/// archive (rare for an APK) rather than guessing.
/// </summary>
public static class ZipAlignment
{
    private const uint EocdSig = 0x06054b50;
    private const uint CentralSig = 0x02014b50;
    private const int Page16k = 16 * 1024;

    public static ZipAlignmentInfo? Probe(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            long len = fs.Length;
            if (len < 22) return null;

            // Find the EOCD by scanning the tail (comment can push it up to 65535 bytes from the end).
            int tail = (int)Math.Min(len, 22 + 65535);
            var buf = new byte[tail];
            fs.Seek(len - tail, SeekOrigin.Begin);
            ReadExact(fs, buf, 0, tail);

            int eocd = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i)) == EocdSig) { eocd = i; break; }
            if (eocd < 0) return null;

            uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eocd + 12));
            uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eocd + 16));
            if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF) return null; // Zip64 — not modelled

            var cd = new byte[cdSize];
            fs.Seek(cdOffset, SeekOrigin.Begin);
            ReadExact(fs, cd, 0, (int)cdSize);

            var info = new ZipAlignmentInfo { Aligned4 = true };
            bool sawNativeLib = false;
            bool nativeAligned = true;
            var local = new byte[30];

            int pos = 0;
            while (pos + 46 <= cd.Length)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos)) != CentralSig) break;

                ushort method = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 10));
                ushort fnLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 28));
                ushort exLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 30));
                ushort cmLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 32));
                uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 42));
                string name = System.Text.Encoding.UTF8.GetString(cd, pos + 46, Math.Min(fnLen, cd.Length - (pos + 46)));

                if (method == 0) // stored — the only entries zipalign aligns
                {
                    fs.Seek(localOffset, SeekOrigin.Begin);
                    if (ReadExact(fs, local, 0, 30))
                    {
                        ushort lFn = BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(26));
                        ushort lEx = BinaryPrimitives.ReadUInt16LittleEndian(local.AsSpan(28));
                        long dataOffset = localOffset + 30 + lFn + lEx;

                        if (dataOffset % 4 != 0) { info.Aligned4 = false; info.MisalignedCount++; }

                        if (name.StartsWith("lib/", StringComparison.Ordinal) && name.EndsWith(".so", StringComparison.Ordinal))
                        {
                            sawNativeLib = true;
                            if (dataOffset % Page16k != 0) nativeAligned = false;
                        }
                    }
                }

                pos += 46 + fnLen + exLen + cmLen;
            }

            info.NativeLibs16k = sawNativeLib ? nativeAligned : null;
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadExact(Stream s, byte[] buf, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, offset + read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
