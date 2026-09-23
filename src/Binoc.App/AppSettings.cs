using System.IO;
using System.Text.Json;

namespace Binoc.App;

/// <summary>
/// Tiny local-only preferences file at <c>%APPDATA%\binoc\settings.json</c> — just what the "what's new"
/// flow needs: the last version that ran here, and whether to surface the changelog on update at all. Same
/// best-effort I/O contract as <see cref="History.HistoryStore"/>: a missing/corrupt file yields defaults
/// rather than an error, and an unwritable folder simply doesn't persist. Nothing here leaves the machine.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string DefaultPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "binoc", "settings.json");

    // Not serialised: where this instance loads/saves. Ignored so it never lands in the JSON.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Path { get; set; } = DefaultPath;

    /// <summary>The app version that last ran here. Null on a fresh install (nothing to diff against).</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Whether to pop the changelog after an update. The popup's "Don't show…" button flips this off.</summary>
    public bool ShowChangelogOnUpdate { get; set; } = true;

    /// <summary>Loads the saved settings (or defaults). Never throws.</summary>
    public static AppSettings Load(string? path = null)
    {
        var target = path ?? DefaultPath;
        try
        {
            if (File.Exists(target))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(target));
                if (loaded is not null) { loaded.Path = target; return loaded; }
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new AppSettings { Path = target };
    }

    /// <summary>Persists the current values. Never throws.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Best-effort: if the folder is unwritable, the preference just won't persist this session.
        }
    }
}
