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

    /// <summary>
    /// Builds a DEX defining one class with the given method and field names, so the reader's class_data walk
    /// (method/field counting) can be exercised. Fields become instance fields; methods become direct methods.
    /// </summary>
    public static byte[] BuildClassWithMembers(string classDescriptor, string[] methodNames, string[] fieldNames)
    {
        int f = fieldNames.Length, m = methodNames.Length;
        // strings: [descriptor, ...fields, ...methods]
        var strings = new List<string> { classDescriptor };
        strings.AddRange(fieldNames);
        strings.AddRange(methodNames);
        int n = strings.Count;

        const int headerSize = 112;
        int stringIdsOff = headerSize;
        int typeIdsOff = stringIdsOff + n * 4;
        int fieldIdsOff = typeIdsOff + 1 * 4;
        int methodIdsOff = fieldIdsOff + f * 8;
        int classDefsOff = methodIdsOff + m * 8;
        int classDataOff = classDefsOff + 1 * 32;

        // class_data_item: 0 static, f instance, m direct, 0 virtual; then delta-encoded members.
        var cd = new List<byte>();
        cd.AddRange(Uleb128(0)); cd.AddRange(Uleb128(f)); cd.AddRange(Uleb128(m)); cd.AddRange(Uleb128(0));
        for (int i = 0; i < f; i++) { cd.AddRange(Uleb128(i == 0 ? 0 : 1)); cd.AddRange(Uleb128(0)); }        // field_idx_diff, access
        for (int i = 0; i < m; i++) { cd.AddRange(Uleb128(i == 0 ? 0 : 1)); cd.AddRange(Uleb128(0)); cd.AddRange(Uleb128(0)); } // method_idx_diff, access, code_off

        int dataOff = classDataOff + cd.Count;

        var data = new List<byte>();
        var stringDataOffsets = new int[n];
        for (int i = 0; i < n; i++)
        {
            stringDataOffsets[i] = dataOff + data.Count;
            var utf8 = System.Text.Encoding.ASCII.GetBytes(strings[i]);
            data.AddRange(Uleb128(utf8.Length));
            data.AddRange(utf8);
            data.Add(0x00);
        }

        var dex = new byte[dataOff + data.Count];
        new byte[] { 0x64, 0x65, 0x78, 0x0a, 0x30, 0x33, 0x35, 0x00 }.CopyTo(dex, 0);

        W(dex, 56, (uint)n); W(dex, 60, (uint)stringIdsOff);
        W(dex, 64, 1); W(dex, 68, (uint)typeIdsOff);
        W(dex, 80, (uint)f); W(dex, 84, (uint)fieldIdsOff);
        W(dex, 88, (uint)m); W(dex, 92, (uint)methodIdsOff);
        W(dex, 96, 1); W(dex, 100, (uint)classDefsOff);

        for (int i = 0; i < n; i++) W(dex, stringIdsOff + i * 4, (uint)stringDataOffsets[i]);
        W(dex, typeIdsOff, 0);                                   // type 0 → descriptor (string 0)
        for (int i = 0; i < f; i++) W(dex, fieldIdsOff + i * 8 + 4, (uint)(1 + i));            // field name_idx
        for (int i = 0; i < m; i++) W(dex, methodIdsOff + i * 8 + 4, (uint)(1 + f + i));       // method name_idx
        W(dex, classDefsOff, 0);                                 // class_idx = type 0
        W(dex, classDefsOff + 24, (uint)classDataOff);           // class_data_off
        cd.CopyTo(dex, classDataOff);
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
