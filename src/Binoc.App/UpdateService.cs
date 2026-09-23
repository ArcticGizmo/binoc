using Velopack;
using Velopack.Sources;

namespace Binoc.App;

/// <summary>
/// The in-app updater, over the GitHub release feed (the same assets <c>release.yml</c> publishes). binoc's
/// trimmed take on Perch's UpdateService: <b>manual only</b> — nothing checks on startup or in the
/// background. A "Check for updates" click calls <see cref="CheckAsync"/>; if that finds a newer version the
/// button turns into "Update", whose click calls <see cref="ApplyAsync"/> to download, apply and restart.
///
/// Checking works on any installed shape, but <i>applying</i> is only ours to do on a real Velopack
/// (Setup) install — never a hand-extracted portable zip or a dev <c>dotnet run</c>. <see cref="SelfUpdates"/>
/// gates the apply path; <see cref="CanCheck"/> gates the check. Constructing the manager does no I/O, so
/// detection is a cheap cached probe that collapses any failure to "not installed" rather than erroring.
/// </summary>
internal sealed class UpdateService
{
    private UpdateManager? _mgr;
    private UpdateInfo? _pending;   // the update found by the last CheckAsync, reused by ApplyAsync
    private InstallState? _state;

    private enum InstallState { Setup, Portable, Unpackaged }

    // Cheap to build (no network until a check/download call), so a single lazily-created instance is fine.
    private UpdateManager Manager => _mgr ??= new UpdateManager(new GithubSource(AppInfo.RepoUrl, null, false));

    private InstallState State => _state ??= Detect();

    /// <summary>True when the update feed can be queried at all — false for a dev run.</summary>
    public bool CanCheck => State != InstallState.Unpackaged;

    /// <summary>True when binoc may download and apply its own updates — i.e. a real Velopack (Setup) install.</summary>
    public bool SelfUpdates => State == InstallState.Setup;

    /// <summary>Why the update path is unavailable, for the button's tooltip on a non-self-updating copy.</summary>
    public string UnavailableReason => State switch
    {
        InstallState.Portable => "This is a portable copy of binoc — download the new release and replace it.",
        _                     => "This build isn't installed (a dev run), so it can't check for updates.",
    };

    /// <summary>The newer version's string (e.g. "0.1.1") if one is available, else null. Throws on a
    /// network/feed error so the caller can surface it. Guard with <see cref="CanCheck"/> first.</summary>
    public async Task<string?> CheckAsync()
    {
        var info = await Manager.CheckForUpdatesAsync();
        _pending = info;
        return info?.TargetFullRelease.Version.ToString();
    }

    /// <summary>Downloads and applies the pending update, then restarts binoc — this <b>exits the process</b>
    /// on success. Only valid when <see cref="SelfUpdates"/> is true. Re-checks if nothing is cached; a no-op
    /// if the update has since gone away.</summary>
    public async Task ApplyAsync()
    {
        var info = _pending ?? await Manager.CheckForUpdatesAsync();
        if (info is null) return;
        await Manager.DownloadUpdatesAsync(info);
        Manager.ApplyUpdatesAndRestart(info); // replaces the process
    }

    private InstallState Detect()
    {
        try
        {
            var mgr = Manager;
            if (!mgr.IsInstalled) return InstallState.Unpackaged;
            return mgr.IsPortable ? InstallState.Portable : InstallState.Setup;
        }
        catch
        {
            return InstallState.Unpackaged;
        }
    }
}
