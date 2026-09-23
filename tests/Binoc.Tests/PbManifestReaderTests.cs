using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class PbManifestReaderTests
{
    // <manifest package versionCode versionName>
    //   <uses-sdk minSdkVersion targetSdkVersion/>
    //   <application debuggable/>
    // </manifest>
    private static byte[] SampleProtoManifest(bool debuggable)
    {
        var usesSdk = PbManifestTestBuilder.Node(PbManifestTestBuilder.Element("uses-sdk",
            new[]
            {
                PbManifestTestBuilder.IntAttr("minSdkVersion", 24),
                PbManifestTestBuilder.IntAttr("targetSdkVersion", 34),
            },
            Array.Empty<byte[]>()));

        var application = PbManifestTestBuilder.Node(PbManifestTestBuilder.Element("application",
            new[] { PbManifestTestBuilder.BoolAttr("debuggable", debuggable) },
            Array.Empty<byte[]>()));

        var manifest = PbManifestTestBuilder.Element("manifest",
            new[]
            {
                PbManifestTestBuilder.StringAttr("package", "com.example.bundle"),
                PbManifestTestBuilder.IntAttr("versionCode", 88),
                PbManifestTestBuilder.StringAttr("versionName", "9.9"),
            },
            new[] { usesSdk, application });

        return PbManifestTestBuilder.Node(manifest);
    }

    [Fact]
    public void Reads_manifest_attributes_and_children()
    {
        var elements = PbManifestReader.Parse(SampleProtoManifest(debuggable: false));

        var m = elements.Single(e => e.Name == "manifest");
        Assert.Equal("com.example.bundle", m.Attributes["package"]);
        Assert.Equal("88", m.Attributes["versionCode"]);   // from compiled Primitive int
        Assert.Equal("9.9", m.Attributes["versionName"]);

        var u = elements.Single(e => e.Name == "uses-sdk");
        Assert.Equal("24", u.Attributes["minSdkVersion"]);
        Assert.Equal("34", u.Attributes["targetSdkVersion"]);

        var a = elements.Single(e => e.Name == "application");
        Assert.Equal("false", a.Attributes["debuggable"]);
    }

    public static byte[] Sample(bool debuggable) => SampleProtoManifest(debuggable);
}
