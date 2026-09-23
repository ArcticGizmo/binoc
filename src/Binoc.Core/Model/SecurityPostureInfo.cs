namespace Binoc.Core.Model;

/// <summary>
/// Security posture (findings §"Security posture"): the "should I worry?" surface. Android — dangerous
/// permissions, exported components, cleartext traffic. iOS — usage-description prompts, ATS exceptions,
/// URL schemes, privacy manifest. Plus a cross-format heuristic scan for embedded secrets (reported by
/// kind and location only — never the value).
/// </summary>
public sealed class SecurityPostureInfo
{
    // ── Android ──
    public List<PermissionInfo> Permissions { get; } = new();
    public List<ExportedComponent> ExportedComponents { get; } = new();
    public bool? UsesCleartextTraffic { get; set; }

    // ── iOS ──
    public List<UsageDescription> UsageDescriptions { get; } = new();
    public bool? AtsAllowsArbitraryLoads { get; set; }
    public List<string> AtsExceptionDomains { get; } = new();
    public List<string> UrlSchemes { get; } = new();
    public bool? HasPrivacyManifest { get; set; }

    // ── Cross-format ──
    public List<SecretFinding> PotentialSecrets { get; } = new();

    /// <summary>Dangerous permissions, for a quick count/highlight.</summary>
    public IEnumerable<PermissionInfo> DangerousPermissions => Permissions.Where(p => p.Dangerous);

    /// <summary>True when any posture signal was found — used to drop an otherwise-empty category.</summary>
    public bool HasAny =>
        Permissions.Count > 0 || ExportedComponents.Count > 0 || UsesCleartextTraffic is not null ||
        UsageDescriptions.Count > 0 || AtsAllowsArbitraryLoads is not null || AtsExceptionDomains.Count > 0 ||
        UrlSchemes.Count > 0 || HasPrivacyManifest is not null || PotentialSecrets.Count > 0;
}

/// <summary>A declared permission and whether it's in the dangerous/runtime/special class.</summary>
public sealed record PermissionInfo(string Name, bool Dangerous);

/// <summary>An explicitly exported app component.</summary>
/// <param name="Type">activity / service / receiver / provider.</param>
/// <param name="Name">The component's class name.</param>
public sealed record ExportedComponent(string Type, string Name);

/// <summary>An iOS purpose-string prompt: the Info.plist key and the human-facing reason.</summary>
public sealed record UsageDescription(string Key, string Purpose);

/// <summary>A potential embedded secret — located by kind and file only; the value is never captured.</summary>
public sealed record SecretFinding(string File, string Kind);
