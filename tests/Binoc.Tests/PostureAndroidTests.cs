using System.IO.Compression;
using Binoc.Core.Analysis;
using Xunit;

namespace Binoc.Tests;

public class PostureAndroidTests
{
    // Builds an APK whose AXML manifest declares permissions, an exported activity, and cleartext traffic.
    private static string WriteApk()
    {
        var strings = new List<string>
        {
            "manifest", "uses-permission", "application", "activity",   // 0-3 element names
            "name", "usesCleartextTraffic", "exported",                 // 4-6 attr names
            "android.permission.CAMERA",                                // 7
            "android.permission.INTERNET",                              // 8 (not dangerous)
            "android.permission.ACCESS_FINE_LOCATION",                  // 9
            "com.example.PublicActivity",                               // 10
            "true",                                                     // 11
        };
        var b = new AxmlTestBuilder(strings);

        var manifest = b.StartElement(0, Array.Empty<AxmlTestBuilder.Attr>());
        var permCamera = b.StartElement(1, new[] { AxmlTestBuilder.StringAttr(4, 7) });
        var permInternet = b.StartElement(1, new[] { AxmlTestBuilder.StringAttr(4, 8) });
        var permLocation = b.StartElement(1, new[] { AxmlTestBuilder.StringAttr(4, 9) });
        var application = b.StartElement(2, new[] { AxmlTestBuilder.StringAttr(5, 11) }); // usesCleartextTraffic="true"
        var activity = b.StartElement(3, new[]
        {
            AxmlTestBuilder.StringAttr(4, 10),  // name
            AxmlTestBuilder.StringAttr(6, 11),  // exported="true"
        });

        var axml = AxmlTestBuilder.File(b.BuildStringPool(), manifest, permCamera, permInternet, permLocation, application, activity);

        var path = Path.Combine(Path.GetTempPath(), $"binoc-posture-{Guid.NewGuid():N}.apk");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var m = zip.CreateEntry("AndroidManifest.xml").Open()) m.Write(axml);
        using (var d = zip.CreateEntry("classes.dex").Open()) d.Write(new byte[] { 1 });
        return path;
    }

    [Fact]
    public void Reports_permissions_exports_and_cleartext()
    {
        var path = WriteApk();
        try
        {
            var report = AnalysisPipeline.Analyze(path);

            Assert.NotNull(report.SecurityPosture);
            var p = report.SecurityPosture!;
            Assert.Equal(3, p.Permissions.Count);
            Assert.Equal(2, p.DangerousPermissions.Count()); // CAMERA + ACCESS_FINE_LOCATION (INTERNET is not)
            Assert.Contains(p.Permissions, x => x.Name.EndsWith("CAMERA") && x.Dangerous);
            Assert.Contains(p.Permissions, x => x.Name.EndsWith("INTERNET") && !x.Dangerous);

            var exported = Assert.Single(p.ExportedComponents);
            Assert.Equal("activity", exported.Type);
            Assert.Equal("com.example.PublicActivity", exported.Name);

            Assert.Equal(true, p.UsesCleartextTraffic);
            Assert.Contains(report.Notes, n => n.Category == "posture" && n.Message.Contains("cleartext", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }
}
