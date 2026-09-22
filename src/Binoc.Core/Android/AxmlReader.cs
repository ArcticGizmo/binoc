namespace Binoc.Core.Android;

/// <summary>
/// A focused reader for Android binary XML (AXML) — the compiled form of <c>AndroidManifest.xml</c> in an
/// APK. It parses the string pool, the resource-map chunk (so framework attributes that carry no name
/// string can still be identified by their resource id), and the XML start-element chunks, exposing a flat
/// list of elements with their attributes as strings. It reads only what identity needs; unknown chunks are
/// skipped by their declared size, so it degrades rather than throws on shapes it doesn't model.
///
/// <para>Pure managed, no dependencies (decision D1). Format reference: AOSP
/// <c>ResourceTypes.h</c> (ResStringPool, ResXMLTree_node/attrExt, ResChunk_header).</para>
/// </summary>
public sealed class AxmlReader
{
    // Chunk types.
    private const ushort ChunkStringPool = 0x0001;
    private const ushort ChunkResourceMap = 0x0180;
    private const ushort ChunkStartElement = 0x0102;

    // Typed-value data types (Res_value.dataType).
    private const byte TypeReference = 0x01;
    private const byte TypeString = 0x03;
    private const byte TypeIntDec = 0x10;
    private const byte TypeIntHex = 0x11;
    private const byte TypeIntBool = 0x12;

    // Well-known android framework attribute resource ids, for when the attribute name string is empty.
    private static readonly Dictionary<uint, string> KnownAttrIds = new()
    {
        [0x0101020c] = "minSdkVersion",
        [0x01010270] = "targetSdkVersion",
        [0x0101021b] = "versionCode",
        [0x0101021c] = "versionName",
        [0x0101000f] = "debuggable",
        [0x01010572] = "compileSdkVersion",
        [0x01010573] = "compileSdkVersionCodename",
    };

    private readonly byte[] _data;
    private string[] _strings = Array.Empty<string>();
    private uint[] _resourceMap = Array.Empty<uint>();

    private AxmlReader(byte[] data) => _data = data;

    /// <summary>An element occurrence in document order: its tag name and its attributes (name → string
    /// value). Namespace prefixes are dropped — a manifest's attributes are keyed by local name.</summary>
    public sealed record Element(string Name, IReadOnlyDictionary<string, string> Attributes);

    /// <summary>Parse the AXML bytes into a flat, in-document-order list of elements. Returns an empty list
    /// if the bytes aren't AXML (wrong magic) or no string pool is found.</summary>
    public static IReadOnlyList<Element> Parse(byte[] data)
    {
        if (data.Length < 8) return Array.Empty<Element>();
        // File header: type 0x0003, headerSize 0x0008 — the AXML magic word 0x00080003.
        if (ReadU16(data, 0) != 0x0003) return Array.Empty<Element>();

        return new AxmlReader(data).ParseInternal();
    }

    private IReadOnlyList<Element> ParseInternal()
    {
        var elements = new List<Element>();

        // Walk top-level chunks starting after the 8-byte file header.
        int pos = 8;
        while (pos + 8 <= _data.Length)
        {
            ushort type = ReadU16(_data, pos);
            uint size = ReadU32(_data, pos + 4);
            if (size < 8 || pos + (long)size > _data.Length) break; // malformed — stop, keep what we have

            switch (type)
            {
                case ChunkStringPool: ParseStringPool(pos); break;
                case ChunkResourceMap: ParseResourceMap(pos, (int)size); break;
                case ChunkStartElement: if (ParseStartElement(pos) is { } el) elements.Add(el); break;
            }

            pos += (int)size;
        }

        return elements;
    }

    private void ParseStringPool(int chunkStart)
    {
        int stringCount = (int)ReadU32(_data, chunkStart + 8);
        uint flags = ReadU32(_data, chunkStart + 16);
        int stringsStart = (int)ReadU32(_data, chunkStart + 20);
        bool utf8 = (flags & 0x100) != 0;

        int offsetsStart = chunkStart + 28; // header(8) + stringCount(4)+styleCount(4)+flags(4)+stringsStart(4)+stylesStart(4)
        var result = new string[stringCount];
        for (int i = 0; i < stringCount; i++)
        {
            int strOffset = (int)ReadU32(_data, offsetsStart + i * 4);
            int at = chunkStart + stringsStart + strOffset;
            result[i] = at >= 0 && at < _data.Length ? ReadPoolString(at, utf8) : string.Empty;
        }
        _strings = result;
    }

    private string ReadPoolString(int at, bool utf8)
    {
        try
        {
            if (utf8)
            {
                // UTF-8: [char-count][byte-count][bytes][0x00]. Both counts use the 1-or-2-byte scheme.
                (_, int c1) = DecodeLen8(at);
                (int byteLen, int c2) = DecodeLen8(at + c1);
                int start = at + c1 + c2;
                return System.Text.Encoding.UTF8.GetString(_data, start, byteLen);
            }
            else
            {
                // UTF-16: [unit-count (1-2 units)][units][0x0000].
                (int unitLen, int consumed) = DecodeLen16(at);
                int start = at + consumed;
                return System.Text.Encoding.Unicode.GetString(_data, start, unitLen * 2);
            }
        }
        catch { return string.Empty; }
    }

    // UTF-8 length: high bit of the first byte extends it to two bytes.
    private (int len, int consumed) DecodeLen8(int at)
    {
        byte b0 = _data[at];
        if ((b0 & 0x80) != 0) return (((b0 & 0x7F) << 8) | _data[at + 1], 2);
        return (b0, 1);
    }

    // UTF-16 length: high bit of the first u16 extends it to two u16s.
    private (int len, int consumed) DecodeLen16(int at)
    {
        ushort w0 = ReadU16(_data, at);
        if ((w0 & 0x8000) != 0) return ((((w0 & 0x7FFF) << 16) | ReadU16(_data, at + 2)), 4);
        return (w0, 2);
    }

    private void ParseResourceMap(int chunkStart, int size)
    {
        int headerSize = ReadU16(_data, chunkStart + 2);
        int count = (size - headerSize) / 4;
        if (count <= 0) return;
        var map = new uint[count];
        for (int i = 0; i < count; i++)
            map[i] = ReadU32(_data, chunkStart + headerSize + i * 4);
        _resourceMap = map;
    }

    private Element? ParseStartElement(int chunkStart)
    {
        int headerSize = ReadU16(_data, chunkStart + 2);
        int extStart = chunkStart + headerSize; // ResXMLTree_attrExt begins after the node header

        int nameRef = (int)ReadU32(_data, extStart + 4);
        int attrStart = ReadU16(_data, extStart + 8);
        int attrSize = ReadU16(_data, extStart + 10);
        int attrCount = ReadU16(_data, extStart + 12);
        if (attrSize == 0) attrSize = 20; // aapt always writes 0x14

        string name = Str(nameRef);
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);

        int attrBase = extStart + attrStart;
        for (int i = 0; i < attrCount; i++)
        {
            int a = attrBase + i * attrSize;
            if (a + 20 > _data.Length) break;

            int aNameRef = (int)ReadU32(_data, a + 4);
            int rawValueRef = (int)ReadU32(_data, a + 8);
            byte dataType = _data[a + 15];
            uint data = ReadU32(_data, a + 16);

            string attrName = ResolveAttrName(aNameRef);
            if (attrName.Length == 0) continue;

            attrs[attrName] = FormatValue(dataType, data, rawValueRef);
        }

        return new Element(name, attrs);
    }

    // An attribute name is usually a pool string; when aapt2 omits it, fall back to the framework resource id.
    private string ResolveAttrName(int nameRef)
    {
        string s = Str(nameRef);
        if (s.Length > 0) return s;
        if (nameRef >= 0 && nameRef < _resourceMap.Length && KnownAttrIds.TryGetValue(_resourceMap[nameRef], out var known))
            return known;
        return string.Empty;
    }

    private string FormatValue(byte dataType, uint data, int rawValueRef) => dataType switch
    {
        TypeString => rawValueRef >= 0 ? Str(rawValueRef) : Str((int)data),
        TypeIntBool => data != 0 ? "true" : "false",
        TypeIntDec => unchecked((int)data).ToString(),
        TypeIntHex => "0x" + data.ToString("x"),
        TypeReference => "@0x" + data.ToString("x8"),
        _ => unchecked((int)data).ToString(),
    };

    private string Str(int index) => index >= 0 && index < _strings.Length ? _strings[index] : string.Empty;

    private static ushort ReadU16(byte[] b, int at) => (ushort)(b[at] | (b[at + 1] << 8));
    private static uint ReadU32(byte[] b, int at) =>
        (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));
}
