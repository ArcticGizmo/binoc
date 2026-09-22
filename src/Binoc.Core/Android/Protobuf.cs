namespace Binoc.Core.Android;

/// <summary>
/// A minimal protobuf wire-format reader — just enough to walk the aapt2 <c>XmlNode</c> messages in an
/// AAB's protobuf manifest (decision D1: no <c>Google.Protobuf</c> dependency, and no need to model the
/// full bundletool schema). It yields the fields of a message region as (number, wire type, payload),
/// leaving interpretation to the caller. Bounds are checked; a malformed region simply stops yielding.
/// </summary>
internal static class Protobuf
{
    public const int WireVarint = 0;
    public const int WireI64 = 1;
    public const int WireLen = 2;
    public const int WireI32 = 5;

    /// <summary>One field: its number and wire type, the varint value (for wire type 0), and the payload
    /// span (<see cref="PayloadStart"/>/<see cref="PayloadLen"/>) for length-delimited/fixed fields.</summary>
    public readonly record struct Field(int Number, int Wire, ulong VarintValue, int PayloadStart, int PayloadLen);

    public static IEnumerable<Field> Fields(byte[] d, int start, int end)
    {
        int pos = start;
        while (pos < end)
        {
            if (!TryReadVarint(d, ref pos, end, out ulong tag)) yield break;
            int number = (int)(tag >> 3);
            int wire = (int)(tag & 7);

            switch (wire)
            {
                case WireVarint:
                    if (!TryReadVarint(d, ref pos, end, out ulong v)) yield break;
                    yield return new Field(number, wire, v, 0, 0);
                    break;
                case WireI64:
                    if (pos + 8 > end) yield break;
                    yield return new Field(number, wire, 0, pos, 8);
                    pos += 8;
                    break;
                case WireI32:
                    if (pos + 4 > end) yield break;
                    yield return new Field(number, wire, 0, pos, 4);
                    pos += 4;
                    break;
                case WireLen:
                    if (!TryReadVarint(d, ref pos, end, out ulong len)) yield break;
                    int l = (int)len;
                    if (l < 0 || pos + l > end) yield break;
                    yield return new Field(number, wire, 0, pos, l);
                    pos += l;
                    break;
                default:
                    yield break; // unknown wire type — can't reliably continue
            }
        }
    }

    public static string Utf8(byte[] d, in Field f) =>
        f.Wire == WireLen ? System.Text.Encoding.UTF8.GetString(d, f.PayloadStart, f.PayloadLen) : string.Empty;

    private static bool TryReadVarint(byte[] d, ref int pos, int end, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < end)
        {
            byte b = d[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
            if (shift > 63) return false;
        }
        return false;
    }
}
