using System.Buffers.Binary;

namespace Binoc.Core.Android;

/// <summary>
/// Locates and parses the <b>APK Signing Block</b> (the v2/v3/v3.1 signatures that sit between the ZIP
/// entries and the central directory) and extracts the first signer's certificate. Pure managed, seek-based.
///
/// <para>Block layout (AOSP <c>ApkSigningBlockUtils</c>): <c>uint64 size</c> · repeated
/// <c>{ uint64 pairLen; uint32 id; value }</c> · <c>uint64 size</c> · 16-byte magic "APK Sig Block 42".
/// A scheme's value is a chain of <c>uint32</c>-length-prefixed blocks: signers → signer → signed-data →
/// (digests, certificates, …); the first certificate in signed-data is the signer's.</para>
/// </summary>
public static class ApkSignatureReader
{
    private const uint EocdSig = 0x06054b50;
    private static readonly byte[] BlockMagic = "APK Sig Block 42"u8.ToArray();

    private const uint IdV2 = 0x7109871a;
    private const uint IdV3 = 0xf05368c0;
    private const uint IdV31 = 0x1b93ad61;

    public sealed class Result
    {
        public bool HasV2 { get; init; }
        public bool HasV3 { get; init; }
        public bool HasV31 { get; init; }
        /// <summary>DER bytes of the first signer's certificate (from v3, else v2), or null.</summary>
        public byte[]? SignerCertDer { get; init; }
    }

    public static Result? Read(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            long len = fs.Length;
            if (len < 22) return null;

            long cdOffset = FindCentralDirectoryOffset(fs, len);
            if (cdOffset < 24) return null;

            // Magic sits in the 16 bytes immediately before the central directory.
            fs.Seek(cdOffset - 16, SeekOrigin.Begin);
            var magic = new byte[16];
            if (!ReadExact(fs, magic, 16) || !magic.AsSpan().SequenceEqual(BlockMagic)) return null;

            // Trailing size (8 bytes before the magic) gives the block start.
            fs.Seek(cdOffset - 24, SeekOrigin.Begin);
            var sizeBuf = new byte[8];
            ReadExact(fs, sizeBuf, 8);
            long blockSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(sizeBuf);
            long blockStart = cdOffset - blockSize - 8;
            if (blockStart < 0 || blockSize > 64 * 1024 * 1024) return null;

            // Read the whole block (signing blocks are small — a few KB to low MB).
            long innerLen = cdOffset - 24 - (blockStart + 8); // pairs region, between the two size fields
            if (innerLen <= 0 || innerLen > blockSize) return null;
            var block = new byte[innerLen];
            fs.Seek(blockStart + 8, SeekOrigin.Begin);
            if (!ReadExact(fs, block, (int)innerLen)) return null;

            return ParsePairs(block);
        }
        catch
        {
            return null;
        }
    }

    private static Result ParsePairs(byte[] block)
    {
        bool v2 = false, v3 = false, v31 = false;
        byte[]? v2Cert = null, v3Cert = null;

        int pos = 0;
        while (pos + 12 <= block.Length)
        {
            ulong pairLen = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(pos));
            pos += 8;
            if (pairLen < 4 || pos + (long)pairLen > block.Length) break;

            uint id = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(pos));
            int valueStart = pos + 4;
            int valueLen = (int)pairLen - 4;

            switch (id)
            {
                case IdV2: v2 = true; v2Cert = ExtractFirstCert(block, valueStart, valueLen); break;
                case IdV3: v3 = true; v3Cert = ExtractFirstCert(block, valueStart, valueLen); break;
                case IdV31: v31 = true; break;
            }

            pos += (int)pairLen;
        }

        return new Result
        {
            HasV2 = v2, HasV3 = v3, HasV31 = v31,
            SignerCertDer = v3Cert ?? v2Cert, // prefer the newest scheme's signer
        };
    }

    // Navigate value → signers → signer → signed-data → (digests, certificates) → first cert.
    private static byte[]? ExtractFirstCert(byte[] b, int start, int len)
    {
        try
        {
            int p = start, end = start + len;
            if (!EnterBlock(b, ref p, end, out int signersEnd)) return null;   // signers sequence
            if (!EnterBlock(b, ref p, signersEnd, out int signerEnd)) return null; // first signer
            if (!EnterBlock(b, ref p, signerEnd, out int signedDataEnd)) return null; // signed data
            if (!EnterBlock(b, ref p, signedDataEnd, out int digestsEnd)) return null; // digests (skip)
            p = digestsEnd;
            if (!EnterBlock(b, ref p, signedDataEnd, out int certsEnd)) return null;   // certificates
            if (!EnterBlock(b, ref p, certsEnd, out int certEnd)) return null;         // first cert
            return b[p..certEnd];
        }
        catch
        {
            return null;
        }
    }

    // Reads a uint32 length at p; on success advances p past the length prefix and sets contentEnd to the
    // end of the block's content (p now points at the content start).
    private static bool EnterBlock(byte[] b, ref int p, int limit, out int contentEnd)
    {
        contentEnd = 0;
        if (p + 4 > limit) return false;
        uint blockLen = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
        p += 4;
        // A zero-length block is valid (an empty sub-sequence, e.g. no digests) — only reject overrun.
        if (p + (long)blockLen > limit) return false;
        contentEnd = p + (int)blockLen;
        return true;
    }

    private static long FindCentralDirectoryOffset(Stream fs, long len)
    {
        int tail = (int)Math.Min(len, 22 + 65535);
        var buf = new byte[tail];
        fs.Seek(len - tail, SeekOrigin.Begin);
        ReadExact(fs, buf, tail);
        for (int i = tail - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i)) == EocdSig)
                return BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i + 16));
        return -1;
    }

    private static bool ReadExact(Stream s, byte[] buf, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
