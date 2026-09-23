namespace Binoc.Core.Model;

/// <summary>
/// Provisioning (findings §Provisioning, iOS only): what the embedded <c>.mobileprovision</c> says about how
/// the app is distributed and what it's allowed to do — distribution channel, team, entitlements,
/// provisioned devices, and <c>get-task-allow</c> (the debuggable flag).
/// </summary>
public sealed class ProvisioningInfo
{
    /// <summary>The profile's display name.</summary>
    public string? Name { get; set; }

    /// <summary>Development / Ad Hoc / Enterprise (In-House) / App Store — inferred from the profile fields.</summary>
    public string? DistributionType { get; set; }

    public string? TeamName { get; set; }
    public string? TeamId { get; set; }

    /// <summary>The <c>application-identifier</c> entitlement (team-prefixed bundle id).</summary>
    public string? AppId { get; set; }

    /// <summary><c>get-task-allow</c> entitlement — true means the app can be attached to a debugger
    /// (a development trait; distribution builds are false).</summary>
    public bool? GetTaskAllow { get; set; }

    /// <summary>Number of provisioned device UDIDs (development/ad-hoc); zero for App Store / enterprise.</summary>
    public int ProvisionedDeviceCount { get; set; }

    /// <summary>Profile expiry.</summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    /// <summary>The entitlement keys present, sorted — the capabilities the app is granted.</summary>
    public List<string> Entitlements { get; } = new();
}
