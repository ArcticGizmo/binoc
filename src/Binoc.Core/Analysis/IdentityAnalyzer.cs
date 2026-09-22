using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Ios;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Identity (M1): who the app claims to be. Dispatches by format — APK reads the binary
/// <c>AndroidManifest.xml</c> (AXML), AAB the protobuf manifest, IPA the <c>Info.plist</c>. Each fills the
/// shared <see cref="IdentityInfo"/> where the concepts overlap.
/// </summary>
public sealed class IdentityAnalyzer : IAnalyzer
{
    public string Category => "identity";

    public bool AppliesTo(BinaryFormat format) => format != BinaryFormat.Unknown;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        switch (context.Format)
        {
            case BinaryFormat.Apk: AnalyzeApk(context, report); break;
            case BinaryFormat.Aab: AnalyzeAab(context, report); break;
            case BinaryFormat.Ipa: AnalyzeIpa(context, report); break;
        }
    }

    // The app bundle's own Info.plist: Payload/<Name>.app/Info.plist — not a nested framework/extension one.
    private static readonly Regex AppInfoPlist =
        new(@"^Payload/[^/]+\.app/Info\.plist$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void AnalyzeIpa(AnalysisContext context, AnalysisReport report)
    {
        var entry = context.Archive.Entries.FirstOrDefault(e => AppInfoPlist.IsMatch(e.FullName.Replace('\\', '/')));
        if (entry is null)
        {
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "No Payload/<App>.app/Info.plist found in the IPA."));
            return;
        }

        var plist = PlistReader.ParseDict(ReadFully(entry));
        if (plist.Count == 0)
        {
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "Info.plist couldn't be decoded (neither XML nor binary plist)."));
            return;
        }

        var identity = new IdentityInfo
        {
            PackageId = PlistStr(plist, "CFBundleIdentifier"),
            VersionName = PlistStr(plist, "CFBundleShortVersionString"),
            VersionCode = PlistStr(plist, "CFBundleVersion"),
            MinimumOsVersion = PlistStr(plist, "MinimumOSVersion"),
            DeviceFamily = DeviceFamily(plist),
            BuildProvenance = BuildProvenance(plist),
        };
        report.Identity = identity;
    }

    private static string? DeviceFamily(IReadOnlyDictionary<string, object?> plist)
    {
        if (plist.TryGetValue("UIDeviceFamily", out var v) && v is List<object?> list)
        {
            var names = list.Select(x => Convert.ToInt64(x) switch
            {
                1 => "iPhone/iPod", 2 => "iPad", 3 => "Apple TV", 4 => "Apple Watch", _ => "other",
            });
            return string.Join(", ", names.Distinct());
        }
        return null;
    }

    private static string? BuildProvenance(IReadOnlyDictionary<string, object?> plist)
    {
        var parts = new List<string>();
        if (PlistStr(plist, "DTSDKName") is { } sdk) parts.Add(sdk);
        if (PlistStr(plist, "DTXcode") is { } xcode) parts.Add($"Xcode {xcode}");
        if (PlistStr(plist, "DTPlatformVersion") is { } pv) parts.Add($"platform {pv}");
        return parts.Count > 0 ? string.Join("  ·  ", parts) : null;
    }

    private static string? PlistStr(IReadOnlyDictionary<string, object?> plist, string key)
        => plist.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v) : null;

    private static void AnalyzeApk(AnalysisContext context, AnalysisReport report)
    {
        var entry = context.Archive.GetEntry("AndroidManifest.xml");
        if (entry is null)
        {
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "No AndroidManifest.xml found at the archive root."));
            return;
        }

        var elements = AxmlReader.Parse(ReadFully(entry))
            .Select(e => (e.Name, e.Attributes));
        if (!BuildAndroidIdentity(elements, report))
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "AndroidManifest.xml couldn't be decoded as binary XML."));
    }

    private static void AnalyzeAab(AnalysisContext context, AnalysisReport report)
    {
        // The AAB base module carries the protobuf-encoded manifest.
        var entry = context.Archive.GetEntry("base/manifest/AndroidManifest.xml");
        if (entry is null)
        {
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "No base/manifest/AndroidManifest.xml found in the bundle."));
            return;
        }

        var elements = PbManifestReader.Parse(ReadFully(entry))
            .Select(e => (e.Name, e.Attributes));
        if (!BuildAndroidIdentity(elements, report))
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "The bundle's protobuf manifest couldn't be decoded."));
    }

    // Shared Android identity extraction over a manifest element list (APK: AXML; AAB: protobuf).
    private static bool BuildAndroidIdentity(
        IEnumerable<(string Name, IReadOnlyDictionary<string, string> Attrs)> elements, AnalysisReport report)
    {
        var list = elements.ToList();
        if (list.Count == 0) return false;

        (string, IReadOnlyDictionary<string, string>)? Find(string name)
        {
            foreach (var e in list)
                if (string.Equals(e.Name, name, StringComparison.Ordinal)) return e;
            return null;
        }
        static string? Get((string, IReadOnlyDictionary<string, string>)? el, string attr)
            => el is { } e && e.Item2.TryGetValue(attr, out var v) && v.Length > 0 ? v : null;

        var manifest = Find("manifest");
        var usesSdk = Find("uses-sdk");
        var application = Find("application");

        var identity = new IdentityInfo
        {
            PackageId = Get(manifest, "package"),
            VersionName = Get(manifest, "versionName"),
            VersionCode = Get(manifest, "versionCode"),
            CompileSdk = Get(manifest, "compileSdkVersion"),
            MinSdk = Get(usesSdk, "minSdkVersion") ?? Get(manifest, "minSdkVersion"),
            TargetSdk = Get(usesSdk, "targetSdkVersion") ?? Get(manifest, "targetSdkVersion"),
        };
        if (Get(application, "debuggable") is { } dbg)
            identity.IsDebuggable = string.Equals(dbg, "true", StringComparison.OrdinalIgnoreCase);

        report.Identity = identity;

        if (identity.IsDebuggable == true)
            report.Notes.Add(new ReportNote("identity", NoteSeverity.Warning,
                "android:debuggable is set — this is a debug configuration, not a release build."));
        return true;
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
