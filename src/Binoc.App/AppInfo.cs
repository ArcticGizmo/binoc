using System.Reflection;

namespace Binoc.App;

/// <summary>
/// Static, app-wide identity: the running version and the GitHub locations the UI links to. Centralised so
/// the window, the "what's new" popup, and (later) the updater all agree on one source. binoc's trimmed
/// counterpart of Perch's <c>AppInfo</c>.
/// </summary>
internal static class AppInfo
{
    public const string RepoUrl   = "https://github.com/ArcticGizmo/binoc";
    public const string IssuesUrl = RepoUrl + "/issues/new";

    /// <summary>
    /// Human-readable version (e.g. "0.1.0"). Read from the assembly's informational version, stripping any
    /// "+commit" git metadata the SDK appends; falls back to the numeric version.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm  = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus < 0 ? info : info[..plus];
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
