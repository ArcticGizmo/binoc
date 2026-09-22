using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Ios;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Security posture (M4). Android: dangerous permissions, explicitly-exported components, and cleartext
/// traffic, read from the manifest. iOS posture lands in the following step.
/// </summary>
public sealed class PostureAnalyzer : IAnalyzer
{
    private static readonly HashSet<string> ComponentTags = new(StringComparer.Ordinal)
    {
        "activity", "activity-alias", "service", "receiver", "provider",
    };

    public string Category => "posture";

    public bool AppliesTo(BinaryFormat format) => format != BinaryFormat.Unknown;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        switch (context.Format)
        {
            case BinaryFormat.Apk:
            case BinaryFormat.Aab:
                AnalyzeAndroid(context, report);
                break;
            case BinaryFormat.Ipa:
                AnalyzeIos(context, report);
                break;
        }
    }

    private static readonly Regex AppInfoPlist =
        new(@"^Payload/([^/]+\.app)/Info\.plist$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void AnalyzeIos(AnalysisContext context, AnalysisReport report)
    {
        var infoEntry = context.Archive.Entries
            .FirstOrDefault(e => AppInfoPlist.IsMatch(e.FullName.Replace('\\', '/')));
        if (infoEntry is null) return;

        var appDir = AppInfoPlist.Match(infoEntry.FullName.Replace('\\', '/')).Groups[1].Value;
        var plist = PlistReader.ParseDict(ReadFully(infoEntry));

        var posture = new SecurityPostureInfo();

        // Purpose-string prompts: any key ending in UsageDescription.
        foreach (var kv in plist)
            if (kv.Key.EndsWith("UsageDescription", StringComparison.Ordinal))
                posture.UsageDescriptions.Add(new UsageDescription(kv.Key, Convert.ToString(kv.Value) ?? ""));

        // App Transport Security.
        if (plist.TryGetValue("NSAppTransportSecurity", out var atsObj) && atsObj is IReadOnlyDictionary<string, object?> ats)
        {
            if (ats.TryGetValue("NSAllowsArbitraryLoads", out var al) && al is bool alb) posture.AtsAllowsArbitraryLoads = alb;
            if (ats.TryGetValue("NSExceptionDomains", out var ed) && ed is IReadOnlyDictionary<string, object?> domains)
                posture.AtsExceptionDomains.AddRange(domains.Keys);
        }

        // Custom URL schemes.
        if (plist.TryGetValue("CFBundleURLTypes", out var urlTypesObj) && urlTypesObj is List<object?> urlTypes)
            foreach (var t in urlTypes)
                if (t is IReadOnlyDictionary<string, object?> td &&
                    td.TryGetValue("CFBundleURLSchemes", out var schemes) && schemes is List<object?> schemeList)
                    posture.UrlSchemes.AddRange(schemeList.Select(s => Convert.ToString(s) ?? "").Where(s => s.Length > 0));

        // Apple privacy manifest at the app root.
        var privacyRegex = new Regex($@"^Payload/{Regex.Escape(appDir)}/PrivacyInfo\.xcprivacy$", RegexOptions.IgnoreCase);
        posture.HasPrivacyManifest = context.Archive.Entries.Any(e => privacyRegex.IsMatch(e.FullName.Replace('\\', '/')));

        report.SecurityPosture = posture;

        if (posture.AtsAllowsArbitraryLoads == true)
            report.Notes.Add(new ReportNote("posture", NoteSeverity.Warning,
                "NSAllowsArbitraryLoads is true — App Transport Security is disabled, so the app permits cleartext HTTP."));
        if (posture.HasPrivacyManifest == false)
            report.Notes.Add(new ReportNote("posture", NoteSeverity.Info,
                "No app-level PrivacyInfo.xcprivacy — Apple's privacy manifest is absent (required for some APIs/SDKs)."));
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void AnalyzeAndroid(AnalysisContext context, AnalysisReport report)
    {
        var elements = AndroidManifest.Load(context.Archive, context.Format);
        if (elements.Count == 0) return;

        var posture = new SecurityPostureInfo();

        foreach (var el in elements)
        {
            if (el.Name is "uses-permission" or "uses-permission-sdk-23")
            {
                if (Get(el, "name") is { } perm)
                    posture.Permissions.Add(new PermissionInfo(perm, DangerousPermissions.IsDangerous(perm)));
            }
            else if (ComponentTags.Contains(el.Name) &&
                     string.Equals(Get(el, "exported"), "true", StringComparison.OrdinalIgnoreCase))
            {
                posture.ExportedComponents.Add(new ExportedComponent(el.Name, Get(el, "name") ?? "(unnamed)"));
            }
            else if (el.Name == "application")
            {
                if (Get(el, "usesCleartextTraffic") is { } ct)
                    posture.UsesCleartextTraffic = string.Equals(ct, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        // De-dup permissions (a manifest can repeat them across sdk variants), keep declaration order.
        Dedup(posture.Permissions);

        report.SecurityPosture = posture;

        int dangerous = posture.DangerousPermissions.Count();
        if (dangerous > 0)
            report.Notes.Add(new ReportNote("posture", NoteSeverity.Info,
                $"{dangerous} dangerous/high-risk permission{(dangerous == 1 ? "" : "s")} requested."));
        if (posture.ExportedComponents.Count > 0)
            report.Notes.Add(new ReportNote("posture", NoteSeverity.Info,
                $"{posture.ExportedComponents.Count} explicitly-exported component{(posture.ExportedComponents.Count == 1 ? "" : "s")} "
                + "(reachable by other apps). Implicit exports via intent filters aren't counted here."));
        if (posture.UsesCleartextTraffic == true)
            report.Notes.Add(new ReportNote("posture", NoteSeverity.Warning,
                "android:usesCleartextTraffic is true — the app permits unencrypted HTTP."));
    }

    private static void Dedup(List<PermissionInfo> perms)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        perms.RemoveAll(p => !seen.Add(p.Name));
    }

    private static string? Get(AndroidManifest.Element el, string attr)
        => el.Attributes.TryGetValue(attr, out var v) && v.Length > 0 ? v : null;
}
