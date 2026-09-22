using System.Buffers.Binary;
using Binoc.Core.Model;

namespace Binoc.Core.Android;

/// <summary>
/// Reads the fixed-layout header of a <c>.dex</c> file — enough for the counts that matter (method
/// references vs the 64K ceiling, defined classes, string/type/field ids). Only the first 112 bytes are
/// needed, so callers never decompress the whole DEX. Pure managed (decision D1).
///
/// <para>Header offsets (Dalvik executable format, little-endian): string_ids_size @56, type_ids_size @64,
/// field_ids_size @80, method_ids_size @88, class_defs_size @96.</para>
/// </summary>
public static class DexReader
{
    private const int HeaderBytes = 112;
    private static readonly byte[] Magic = { 0x64, 0x65, 0x78, 0x0a }; // "dex\n"

    public static DexFileInfo? Read(string name, byte[] header)
    {
        if (header.Length < HeaderBytes) return null;
        if (!header.AsSpan(0, 4).SequenceEqual(Magic)) return null;

        int stringIds = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(56));
        int typeIds = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(64));
        int fieldIds = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
        int methodIds = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(88));
        int classDefs = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(96));

        return new DexFileInfo(name, methodIds, classDefs, stringIds, typeIds, fieldIds);
    }

    public static int HeaderSize => HeaderBytes;
}
