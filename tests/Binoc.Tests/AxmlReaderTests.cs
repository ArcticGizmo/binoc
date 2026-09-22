using System.Text;
using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

/// <summary>
/// The AXML (binary AndroidManifest.xml) decoder. There's no aapt available to generate fixtures, so these
/// build faithful AXML byte streams via <see cref="AxmlTestBuilder"/> (UTF-8 string pool + typed
/// attributes, exactly as aapt emits) and assert the reader parses them back. The builder doubles as
/// executable documentation of the format the reader targets.
/// </summary>
public class AxmlReaderTests
{
    [Fact]
    public void Reads_manifest_attributes_by_name()
    {
        var strings = new List<string>
        {
            "manifest", "uses-sdk", "application",              // 0,1,2 element names
            "package", "versionCode", "versionName",            // 3,4,5 attr names
            "minSdkVersion", "targetSdkVersion", "debuggable",  // 6,7,8 attr names
            "com.example.app", "1.2.3",                          // 9,10 string values
        };
        var b = new AxmlTestBuilder(strings);

        var manifest = b.StartElement(0, new[]
        {
            AxmlTestBuilder.StringAttr(3, 9),   // package = "com.example.app"
            AxmlTestBuilder.IntAttr(4, 42),     // versionCode = 42
            AxmlTestBuilder.StringAttr(5, 10),  // versionName = "1.2.3"
        });
        var usesSdk = b.StartElement(1, new[]
        {
            AxmlTestBuilder.IntAttr(6, 24),     // minSdkVersion = 24
            AxmlTestBuilder.IntAttr(7, 34),     // targetSdkVersion = 34
        });
        var application = b.StartElement(2, new[]
        {
            AxmlTestBuilder.BoolAttr(8, true),  // debuggable = true
        });

        var axml = AxmlTestBuilder.File(b.BuildStringPool(), manifest, usesSdk, application);

        var elements = AxmlReader.Parse(axml);

        var m = elements.Single(e => e.Name == "manifest");
        Assert.Equal("com.example.app", m.Attributes["package"]);
        Assert.Equal("42", m.Attributes["versionCode"]);
        Assert.Equal("1.2.3", m.Attributes["versionName"]);

        var u = elements.Single(e => e.Name == "uses-sdk");
        Assert.Equal("24", u.Attributes["minSdkVersion"]);
        Assert.Equal("34", u.Attributes["targetSdkVersion"]);

        var a = elements.Single(e => e.Name == "application");
        Assert.Equal("true", a.Attributes["debuggable"]);
    }

    [Fact]
    public void Resolves_attribute_name_from_resource_map_when_name_string_is_empty()
    {
        // aapt2 can leave framework attribute name strings empty and rely on the resource map. Here the
        // attribute at pool index 1 is "" and the resource map maps index 1 -> 0x0101021b (versionCode).
        var strings = new List<string> { "manifest", "" };
        var b = new AxmlTestBuilder(strings);
        var resMap = new uint[] { 0x0, 0x0101021b };   // index 1 -> versionCode

        var manifest = b.StartElement(0, new[]
        {
            AxmlTestBuilder.IntAttr(1, 7),  // name string is "", must resolve via resource map
        });

        var axml = AxmlTestBuilder.File(b.BuildStringPool(), b.BuildResourceMap(resMap), manifest);

        var elements = AxmlReader.Parse(axml);
        var m = elements.Single(e => e.Name == "manifest");
        Assert.Equal("7", m.Attributes["versionCode"]);
    }

    [Fact]
    public void Non_axml_bytes_return_empty()
    {
        Assert.Empty(AxmlReader.Parse(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><manifest/>")));
    }
}
