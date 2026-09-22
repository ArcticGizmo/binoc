using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Binoc.Core.Ios;

/// <summary>
/// Reads an Apple property list into a plain object graph — <see cref="Dictionary{TKey,TValue}"/> for
/// dicts, <see cref="List{T}"/> for arrays, and <see cref="string"/>/<see cref="long"/>/<see cref="double"/>/
/// <see cref="bool"/>/<see cref="byte"/>[]/<see cref="DateTime"/> for scalars. Handles both encodings an
/// <c>Info.plist</c> ships in: XML (<c>&lt;?xml…&gt;</c>) and binary (<c>bplist00</c>). Pure managed, no
/// dependencies (decision D1).
/// </summary>
public static class PlistReader
{
    /// <summary>Parse plist bytes. Returns null if the bytes are neither a binary nor an XML plist.</summary>
    public static object? Parse(byte[] data)
    {
        if (data.Length >= 8 && Encoding.ASCII.GetString(data, 0, 6) == "bplist")
            return BinaryPlist.Parse(data);

        // XML plist: <plist> wraps a single value element (usually a <dict>).
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
            var content = doc.Root?.Elements().FirstOrDefault();
            return content is null ? null : ParseXmlValue(content, doc.Root!);
        }
        catch { return null; }
    }

    /// <summary>Convenience: parse and, if the root is a dict, return it; otherwise an empty dict.</summary>
    public static IReadOnlyDictionary<string, object?> ParseDict(byte[] data)
        => Parse(data) as Dictionary<string, object?> ?? new Dictionary<string, object?>();

    // ── XML plist ────────────────────────────────────────────────────────────────────
    private static object? ParseXmlValue(XElement el, XElement dictContext)
    {
        switch (el.Name.LocalName)
        {
            case "dict": return ParseXmlDict(el);
            case "array": return el.Elements().Select(e => ParseXmlValue(e, el)).ToList();
            case "string": return el.Value;
            case "integer": return long.TryParse(el.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L;
            case "real": return double.TryParse(el.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;
            case "true": return true;
            case "false": return false;
            case "date": return DateTime.TryParse(el.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? dt : (object?)el.Value;
            case "data": return SafeBase64(el.Value);
            default: return el.Value;
        }
    }

    private static Dictionary<string, object?> ParseXmlDict(XElement dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var children = dict.Elements().ToList();
        for (int i = 0; i + 1 < children.Count; i += 2)
        {
            if (children[i].Name.LocalName != "key") continue;
            result[children[i].Value] = ParseXmlValue(children[i + 1], dict);
        }
        return result;
    }

    private static byte[] SafeBase64(string s)
    {
        try { return Convert.FromBase64String(s.Trim()); } catch { return Array.Empty<byte>(); }
    }

    // ── Binary plist (bplist00) ────────────────────────────────────────────────────────
    private sealed class BinaryPlist
    {
        private readonly byte[] _d;
        private readonly int _offsetSize;
        private readonly int _refSize;
        private readonly long[] _offsets;

        private BinaryPlist(byte[] d, int offsetSize, int refSize, long[] offsets)
        {
            _d = d; _offsetSize = offsetSize; _refSize = refSize; _offsets = offsets;
        }

        public static object? Parse(byte[] d)
        {
            if (d.Length < 40) return null; // header(8) + trailer(32)
            int trailer = d.Length - 32;
            int offsetSize = d[trailer + 6];
            int refSize = d[trailer + 7];
            long numObjects = ReadBE(d, trailer + 8, 8);
            long topObject = ReadBE(d, trailer + 16, 8);
            long offsetTableOffset = ReadBE(d, trailer + 24, 8);

            if (numObjects <= 0 || numObjects > (d.Length / Math.Max(1, offsetSize))) return null;

            var offsets = new long[numObjects];
            for (long i = 0; i < numObjects; i++)
                offsets[i] = ReadBE(d, (int)(offsetTableOffset + i * offsetSize), offsetSize);

            return new BinaryPlist(d, offsetSize, refSize, offsets).ReadObject((int)topObject);
        }

        private object? ReadObject(int index)
        {
            if (index < 0 || index >= _offsets.Length) return null;
            int at = (int)_offsets[index];
            if (at < 0 || at >= _d.Length) return null;

            byte marker = _d[at];
            int hi = marker >> 4, lo = marker & 0x0F;

            switch (hi)
            {
                case 0x0:
                    return lo switch { 0x8 => false, 0x9 => true, _ => (object?)null };
                case 0x1: // int, 2^lo bytes
                    return ReadBE(_d, at + 1, 1 << lo);
                case 0x2: // real
                    return ReadReal(at + 1, 1 << lo);
                case 0x3: // date (8-byte big-endian double, seconds since 2001-01-01 UTC)
                    return AppleEpoch.AddSeconds(BitConverter.Int64BitsToDouble(ReadBE(_d, at + 1, 8)));
                case 0x4: // data
                {
                    var (len, start) = ReadLength(at, lo);
                    return _d.AsSpan(start, len).ToArray();
                }
                case 0x5: // ASCII string
                {
                    var (len, start) = ReadLength(at, lo);
                    return Encoding.ASCII.GetString(_d, start, len);
                }
                case 0x6: // UTF-16BE string (lo = unit count)
                {
                    var (len, start) = ReadLength(at, lo);
                    return Encoding.BigEndianUnicode.GetString(_d, start, len * 2);
                }
                case 0x8: // UID — treat as int
                    return ReadBE(_d, at + 1, lo + 1);
                case 0xA: // array
                {
                    var (count, start) = ReadLength(at, lo);
                    var list = new List<object?>(count);
                    for (int i = 0; i < count; i++)
                        list.Add(ReadObject((int)ReadBE(_d, start + i * _refSize, _refSize)));
                    return list;
                }
                case 0xD: // dict
                {
                    var (count, start) = ReadLength(at, lo);
                    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        int keyRef = (int)ReadBE(_d, start + i * _refSize, _refSize);
                        int valRef = (int)ReadBE(_d, start + (count + i) * _refSize, _refSize);
                        if (ReadObject(keyRef) is string key)
                            dict[key] = ReadObject(valRef);
                    }
                    return dict;
                }
                default:
                    return null;
            }
        }

        // For containers/strings/data: resolves the element/byte count and the offset where the payload
        // begins. When lo == 0xF the real count is an int object that immediately follows the marker.
        private (int count, int start) ReadLength(int at, int lo)
        {
            if (lo != 0x0F) return (lo, at + 1);
            byte intMarker = _d[at + 1];
            int intBytes = 1 << (intMarker & 0x0F);
            int count = (int)ReadBE(_d, at + 2, intBytes);
            return (count, at + 2 + intBytes);
        }

        private double ReadReal(int at, int size) => size switch
        {
            4 => BitConverter.Int32BitsToSingle((int)ReadBE(_d, at, 4)),
            8 => BitConverter.Int64BitsToDouble(ReadBE(_d, at, 8)),
            _ => 0d,
        };

        private static readonly DateTime AppleEpoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static long ReadBE(byte[] b, int at, int size)
    {
        long v = 0;
        for (int i = 0; i < size; i++) v = (v << 8) | b[at + i];
        return v;
    }
}
