namespace Binoc.Core.Android;

/// <summary>
/// Parses an AAB's protobuf-encoded <c>AndroidManifest.xml</c> (aapt2's <c>XmlNode</c> message tree) into a
/// flat list of elements with string attributes — the same shape <see cref="AxmlReader"/> produces for an
/// APK, so identity extraction is shared. Only the fields identity needs are decoded; a value is taken from
/// the attribute's string form, falling back to its compiled primitive (int/bool) when the string is empty.
///
/// <para>aapt2 proto shape (Resources.proto): <c>XmlNode{ element=1, text=2 }</c>,
/// <c>XmlElement{ name=3, attribute=4 (repeated), child=5 (repeated) }</c>,
/// <c>XmlAttribute{ name=2, value=3, compiled_item=6 }</c>, <c>Item{ prim=7 }</c>,
/// <c>Primitive{ int_decimal=6, int_hex=7, boolean=8 }</c>.</para>
/// </summary>
public static class PbManifestReader
{
    public sealed record Element(string Name, IReadOnlyDictionary<string, string> Attributes);

    public static IReadOnlyList<Element> Parse(byte[] data)
    {
        var elements = new List<Element>();
        try { WalkNode(data, 0, data.Length, elements); }
        catch { /* tolerate a malformed tail — return what we gathered */ }
        return elements;
    }

    // XmlNode: field 1 is the XmlElement.
    private static void WalkNode(byte[] d, int start, int end, List<Element> outp)
    {
        foreach (var f in Protobuf.Fields(d, start, end))
            if (f.Number == 1 && f.Wire == Protobuf.WireLen)
                WalkElement(d, f.PayloadStart, f.PayloadStart + f.PayloadLen, outp);
    }

    // XmlElement: name (3), attributes (4, repeated), children (5, repeated XmlNode).
    private static void WalkElement(byte[] d, int start, int end, List<Element> outp)
    {
        string name = string.Empty;
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        var childRegions = new List<(int s, int e)>();

        foreach (var f in Protobuf.Fields(d, start, end))
        {
            switch (f.Number)
            {
                case 3 when f.Wire == Protobuf.WireLen:
                    name = Protobuf.Utf8(d, f);
                    break;
                case 4 when f.Wire == Protobuf.WireLen:
                    ReadAttribute(d, f.PayloadStart, f.PayloadStart + f.PayloadLen, attrs);
                    break;
                case 5 when f.Wire == Protobuf.WireLen:
                    childRegions.Add((f.PayloadStart, f.PayloadStart + f.PayloadLen));
                    break;
            }
        }

        outp.Add(new Element(name, attrs));

        // Recurse into children (each is an XmlNode).
        foreach (var (s, e) in childRegions)
            WalkNode(d, s, e, outp);
    }

    // XmlAttribute: name (2), value string (3), compiled_item Item (6).
    private static void ReadAttribute(byte[] d, int start, int end, Dictionary<string, string> attrs)
    {
        string name = string.Empty, value = string.Empty;
        (int s, int e)? compiledItem = null;

        foreach (var f in Protobuf.Fields(d, start, end))
        {
            switch (f.Number)
            {
                case 2 when f.Wire == Protobuf.WireLen: name = Protobuf.Utf8(d, f); break;
                case 3 when f.Wire == Protobuf.WireLen: value = Protobuf.Utf8(d, f); break;
                case 6 when f.Wire == Protobuf.WireLen: compiledItem = (f.PayloadStart, f.PayloadStart + f.PayloadLen); break;
            }
        }

        if (name.Length == 0) return;
        if (value.Length == 0 && compiledItem is { } ci)
            value = ReadCompiledItem(d, ci.s, ci.e) ?? string.Empty;

        attrs[name] = value;
    }

    // Item: field 7 is a Primitive.
    private static string? ReadCompiledItem(byte[] d, int start, int end)
    {
        foreach (var f in Protobuf.Fields(d, start, end))
            if (f.Number == 7 && f.Wire == Protobuf.WireLen)
                return ReadPrimitive(d, f.PayloadStart, f.PayloadStart + f.PayloadLen);
        return null;
    }

    // Primitive: int_decimal (6, varint), int_hex (7, varint), boolean (8, varint).
    private static string? ReadPrimitive(byte[] d, int start, int end)
    {
        foreach (var f in Protobuf.Fields(d, start, end))
        {
            if (f.Wire != Protobuf.WireVarint) continue;
            switch (f.Number)
            {
                case 6: return unchecked((int)f.VarintValue).ToString();
                case 7: return "0x" + f.VarintValue.ToString("x");
                case 8: return f.VarintValue != 0 ? "true" : "false";
            }
        }
        return null;
    }
}
