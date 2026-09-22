using System.Buffers.Binary;

namespace Binoc.Tests;

/// <summary>
/// Builds a minimal but structurally-valid <c>.dex</c> for the type-reader tests: given a set of class
/// descriptors it lays out the header, the string_ids/type_ids/class_defs tables and the string_data region so
/// every descriptor is a <em>defined</em> class. Mirrors the offsets <c>DexTypeReader</c> walks, so it also
/// documents them.
/// </summary>
internal static class DexTestBuilder
{
    // Builds a DEX whose defined classes are exactly the given descriptors (e.g. "Lcom/example/a;").
    public static byte[] Build(params string[] descriptors)
    {
        int n = descriptors.Length;
        const int headerSize = 112;
        int stringIdsOff = headerSize;
        int typeIdsOff = stringIdsOff + n * 4;
        int classDefsOff = typeIdsOff + n * 4;
        int dataOff = classDefsOff + n * 32;

        // Encode the string_data region and remember each item's absolute offset.
        var data = new List<byte>();
        var stringDataOffsets = new int[n];
        for (int i = 0; i < n; i++)
        {
            stringDataOffsets[i] = dataOff + data.Count;
            var utf8 = System.Text.Encoding.ASCII.GetBytes(descriptors[i]);
            data.AddRange(Uleb128(utf8.Length)); // utf16_size (ASCII: == byte length)
            data.AddRange(utf8);
            data.Add(0x00);                      // NUL terminator
        }

        int total = dataOff + data.Count;
        var dex = new byte[total];

        // Magic "dex\n035\0".
        new byte[] { 0x64, 0x65, 0x78, 0x0a, 0x30, 0x33, 0x35, 0x00 }.CopyTo(dex, 0);

        // Only the fields DexTypeReader reads need to be right.
        W(dex, 56, (uint)n); W(dex, 60, (uint)stringIdsOff);   // string_ids size/off
        W(dex, 64, (uint)n); W(dex, 68, (uint)typeIdsOff);     // type_ids size/off
        W(dex, 96, (uint)n); W(dex, 100, (uint)classDefsOff);  // class_defs size/off

        // string_ids: offset to each string_data item.
        for (int i = 0; i < n; i++) W(dex, stringIdsOff + i * 4, (uint)stringDataOffsets[i]);
        // type_ids: 1:1 with strings (type i → descriptor i).
        for (int i = 0; i < n; i++) W(dex, typeIdsOff + i * 4, (uint)i);
        // class_defs: one per type; first u4 is class_idx (the type index).
        for (int i = 0; i < n; i++) W(dex, classDefsOff + i * 32, (uint)i);

        data.CopyTo(dex, dataOff);
        return dex;
    }

    private static void W(byte[] d, int off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(off, 4), v);

    private static IEnumerable<byte> Uleb128(int value)
    {
        uint v = (uint)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            yield return b;
        } while (v != 0);
    }
}
