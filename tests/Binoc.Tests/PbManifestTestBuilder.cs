using System.Text;

namespace Binoc.Tests;

/// <summary>
/// Encodes an aapt2 protobuf <c>AndroidManifest.xml</c> (the form an AAB ships) for the reader tests —
/// the protobuf counterpart of <see cref="AxmlTestBuilder"/>. Mirrors the Resources.proto field numbers the
/// reader targets, so it also documents the schema.
/// </summary>
internal static class PbManifestTestBuilder
{
    // ── wire primitives ──
    private static byte[] Varint(ulong v)
    {
        var b = new List<byte>();
        while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; }
        b.Add((byte)v);
        return b.ToArray();
    }

    private static byte[] Tag(int number, int wire) => Varint(((ulong)number << 3) | (ulong)wire);

    private static byte[] Concat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p, 0, p.Length);
        return ms.ToArray();
    }

    private static byte[] LenField(int number, byte[] payload) => Concat(Tag(number, 2), Varint((ulong)payload.Length), payload);
    private static byte[] StrField(int number, string s) => LenField(number, Encoding.UTF8.GetBytes(s));
    private static byte[] VarintField(int number, ulong value) => Concat(Tag(number, 0), Varint(value));

    // ── aapt messages ──
    private static byte[] PrimitiveInt(int value) => VarintField(6, unchecked((uint)value));   // int_decimal_value
    private static byte[] PrimitiveBool(bool value) => VarintField(8, value ? 1u : 0u);          // boolean_value
    private static byte[] Item(byte[] primitive) => LenField(7, primitive);                       // Item.prim

    public static byte[] StringAttr(string name, string value) => Concat(StrField(2, name), StrField(3, value));
    public static byte[] IntAttr(string name, int value) => Concat(StrField(2, name), LenField(6, Item(PrimitiveInt(value))));
    public static byte[] BoolAttr(string name, bool value) => Concat(StrField(2, name), LenField(6, Item(PrimitiveBool(value))));

    /// <summary>An XmlElement: name (3) + attributes (4, repeated) + child XmlNodes (5, repeated).</summary>
    public static byte[] Element(string name, byte[][] attrs, byte[][] childNodes)
    {
        var parts = new List<byte[]> { StrField(3, name) };
        foreach (var a in attrs) parts.Add(LenField(4, a));
        foreach (var c in childNodes) parts.Add(LenField(5, c));
        return Concat(parts.ToArray());
    }

    /// <summary>An XmlNode wrapping an element (field 1). The top-level manifest is exactly this.</summary>
    public static byte[] Node(byte[] element) => LenField(1, element);
}
