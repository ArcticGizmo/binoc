using Binoc.Core.Android;
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
        }
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
