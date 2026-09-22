using System.Text;

namespace Binoc.Tests;

/// <summary>
/// Emits AXML (binary AndroidManifest.xml) byte streams for the reader tests, matching what aapt produces:
/// a UTF-8 <c>ResStringPool</c>, an optional resource-map chunk, and <c>START_ELEMENT</c> chunks with typed
/// attributes. All little-endian, all chunk sizes 4-aligned.
/// </summary>
internal sealed class AxmlTestBuilder
{
    private readonly IReadOnlyList<string> _strings;

    public AxmlTestBuilder(IReadOnlyList<string> strings) => _strings = strings;

    /// <summary>An attribute: pool index of its name, its raw-value ref, the typed-value dataType, and data.</summary>
    public readonly record struct Attr(int Name, uint Raw, byte Type, uint Data);

    // Typed-value dataTypes.
    public static Attr StringAttr(int name, int strIndex) => new(name, (uint)strIndex, 0x03, (uint)strIndex);
    public static Attr IntAttr(int name, int value) => new(name, 0xFFFFFFFF, 0x10, unchecked((uint)value));
    public static Attr BoolAttr(int name, bool value) => new(name, 0xFFFFFFFF, 0x12, value ? 1u : 0u);

    public byte[] BuildStringPool()
    {
        var data = new MemoryStream();
        var offsets = new uint[_strings.Count];
        for (int i = 0; i < _strings.Count; i++)
        {
            offsets[i] = (uint)data.Position;
            var bytes = Encoding.UTF8.GetBytes(_strings[i]);
            data.WriteByte((byte)_strings[i].Length); // char count (ASCII: == byte count)
            data.WriteByte((byte)bytes.Length);        // byte count
            data.Write(bytes, 0, bytes.Length);
            data.WriteByte(0);                          // NUL terminator
        }
        while (data.Length % 4 != 0) data.WriteByte(0); // 4-align the data region

        int count = _strings.Count;
        int stringsStart = 28 + count * 4;
        var dataBytes = data.ToArray();
        int chunkSize = stringsStart + dataBytes.Length;

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)0x0001);   // type: string pool
        w.Write((ushort)28);       // header size
        w.Write((uint)chunkSize);
        w.Write((uint)count);      // string count
        w.Write((uint)0);          // style count
        w.Write((uint)0x00000100); // flags: UTF-8
        w.Write((uint)stringsStart);
        w.Write((uint)0);          // styles start
        foreach (var off in offsets) w.Write(off);
        w.Write(dataBytes);
        return ms.ToArray();
    }

    public byte[] BuildResourceMap(uint[] ids)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)0x0180);            // type: resource map
        w.Write((ushort)8);                 // header size
        w.Write((uint)(8 + ids.Length * 4));// chunk size
        foreach (var id in ids) w.Write(id);
        return ms.ToArray();
    }

    public byte[] StartElement(int nameIndex, Attr[] attrs)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int chunkSize = 16 + 20 + attrs.Length * 20;
        w.Write((ushort)0x0102);   // type: start element
        w.Write((ushort)16);       // header size
        w.Write((uint)chunkSize);
        w.Write((uint)1);          // line number
        w.Write(0xFFFFFFFF);       // comment
        // attrExt
        w.Write(0xFFFFFFFF);       // ns
        w.Write((uint)nameIndex);  // name
        w.Write((ushort)20);       // attributeStart
        w.Write((ushort)20);       // attributeSize
        w.Write((ushort)attrs.Length);
        w.Write((ushort)0);        // idIndex
        w.Write((ushort)0);        // classIndex
        w.Write((ushort)0);        // styleIndex
        foreach (var a in attrs)
        {
            w.Write(0xFFFFFFFF);   // ns
            w.Write((uint)a.Name); // name
            w.Write(a.Raw);        // rawValue
            w.Write((ushort)8);    // Res_value size
            w.Write((byte)0);      // res0
            w.Write(a.Type);       // dataType
            w.Write(a.Data);       // data
        }
        return ms.ToArray();
    }

    public static byte[] File(params byte[][] chunks)
    {
        var body = new MemoryStream();
        foreach (var c in chunks) body.Write(c, 0, c.Length);
        var bodyBytes = body.ToArray();

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)0x0003);              // type: XML
        w.Write((ushort)8);                   // header size
        w.Write((uint)(8 + bodyBytes.Length));// file size
        w.Write(bodyBytes);
        return ms.ToArray();
    }
}
