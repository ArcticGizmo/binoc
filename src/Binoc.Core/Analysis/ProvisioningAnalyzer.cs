using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Ios;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Provisioning (M3, iOS): reads the embedded <c>.mobileprovision</c> and reports the distribution channel,
/// team, entitlements, provisioned-device count and <c>get-task-allow</c>. Distribution type is inferred:
/// ProvisionsAllDevices → Enterprise; provisioned devices present → Development (get-task-allow) or Ad Hoc;
/// otherwise App Store.
/// </summary>
public sealed class ProvisioningAnalyzer : IAnalyzer
{
    public string Category => "provisioning";

    public bool AppliesTo(BinaryFormat format) => format == BinaryFormat.Ipa;

    private static readonly Regex EmbeddedProvision =
        new(@"^Payload/[^/]+\.app/embedded\.mobileprovision$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var entry = context.Archive.Entries
            .FirstOrDefault(e => EmbeddedProvision.IsMatch(e.FullName.Replace('\\', '/')));
        if (entry is null) return; // App Store IPAs may have it stripped; SigningAnalyzer already notes that

        var mp = MobileProvisionReader.Read(ReadFully(entry));
        if (mp is null) return;
        var p = mp.Plist;

        var entitlements = p.TryGetValue("Entitlements", out var e) && e is IReadOnlyDictionary<string, object?> ent
            ? ent : new Dictionary<string, object?>();

        bool provisionsAll = p.TryGetValue("ProvisionsAllDevices", out var pa) && pa is true;
        int deviceCount = p.TryGetValue("ProvisionedDevices", out var pd) && pd is List<object?> devices ? devices.Count : 0;
        bool? getTaskAllow = entitlements.TryGetValue("get-task-allow", out var g) && g is bool gb ? gb : null;

        var info = new ProvisioningInfo
        {
            Name = Str(p, "Name"),
            TeamName = Str(p, "TeamName"),
            TeamId = FirstOfArray(p, "TeamIdentifier"),
            AppId = entitlements.TryGetValue("application-identifier", out var ai) ? Convert.ToString(ai) : null,
            GetTaskAllow = getTaskAllow,
            ProvisionedDeviceCount = deviceCount,
            ExpirationDate = Date(p, "ExpirationDate"),
            DistributionType = InferType(provisionsAll, deviceCount, getTaskAllow),
        };
        info.Entitlements.AddRange(entitlements.Keys.OrderBy(k => k, StringComparer.Ordinal));

        report.Provisioning = info;

        if (info.GetTaskAllow == true)
            report.Notes.Add(new ReportNote("provisioning", NoteSeverity.Info,
                "get-task-allow is set — this profile permits debugging (a development build trait)."));
        if (info.ExpirationDate is { } exp && exp < DateTimeOffset.UtcNow)
            report.Notes.Add(new ReportNote("provisioning", NoteSeverity.Warning,
                $"The provisioning profile expired on {exp:yyyy-MM-dd}."));
    }

    private static string InferType(bool provisionsAll, int deviceCount, bool? getTaskAllow)
    {
        if (provisionsAll) return "Enterprise (In-House)";
        if (deviceCount > 0) return getTaskAllow == true ? "Development" : "Ad Hoc";
        return "App Store";
    }

    private static string? Str(IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is not null ? Convert.ToString(v) : null;

    private static string? FirstOfArray(IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is List<object?> { Count: > 0 } list ? Convert.ToString(list[0]) : null;

    private static DateTimeOffset? Date(IReadOnlyDictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is DateTime dt ? new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero) : null;

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
