using System.Text;
using Binoc.Core.Ios;
using Xunit;

namespace Binoc.Tests;

public class PlistReaderTests
{
    [Fact]
    public void Parses_an_xml_plist_dict()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
              <key>CFBundleIdentifier</key><string>com.example.ios</string>
              <key>CFBundleVersion</key><string>17</string>
              <key>MinimumOSVersion</key><string>15.0</string>
              <key>UIDeviceFamily</key><array><integer>1</integer><integer>2</integer></array>
            </dict>
            </plist>
            """;
        var dict = PlistReader.ParseDict(Encoding.UTF8.GetBytes(xml));

        Assert.Equal("com.example.ios", dict["CFBundleIdentifier"]);
        Assert.Equal("17", dict["CFBundleVersion"]);
        Assert.Equal("15.0", dict["MinimumOSVersion"]);
        var fam = Assert.IsType<List<object?>>(dict["UIDeviceFamily"]);
        Assert.Equal(new object?[] { 1L, 2L }, fam);
    }

    [Fact]
    public void Parses_a_binary_plist_dict()
    {
        // bplist00 encoding of { "A": "B" } — obj0 dict(1), obj1 "A", obj2 "B"; 1-byte offsets/refs.
        var d = new byte[50];
        Encoding.ASCII.GetBytes("bplist00").CopyTo(d, 0);
        d[8] = 0xD1; d[9] = 0x01; d[10] = 0x02;   // dict, keyref=1, valref=2
        d[11] = 0x51; d[12] = (byte)'A';           // ASCII string "A"
        d[13] = 0x51; d[14] = (byte)'B';           // ASCII string "B"
        d[15] = 8; d[16] = 11; d[17] = 13;         // offset table (offsetSize=1)
        // trailer at 18:
        d[24] = 1;                                 // offsetSize
        d[25] = 1;                                 // refSize
        d[33] = 3;                                 // numObjects (8-byte BE, low byte)
        // topObject = 0 (already zero)
        d[49] = 15;                                // offsetTableOffset (low byte)

        var dict = PlistReader.ParseDict(d);
        Assert.Equal("B", dict["A"]);
    }

    [Fact]
    public void Non_plist_bytes_return_empty_dict()
        => Assert.Empty(PlistReader.ParseDict(Encoding.UTF8.GetBytes("not a plist")));
}
