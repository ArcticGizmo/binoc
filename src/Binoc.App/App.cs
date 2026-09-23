using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Binoc.App.Windows;
using Binoc.Core.Changelog;

namespace Binoc.App;

/// <summary>
/// The Avalonia application. Built entirely in code (no XAML), following Perch's convention: a Fluent base
/// theme pinned to the dark variant, over which binoc's fixed Nord <see cref="Theming.Palette"/> paints the
/// chrome. There is only ever one window (<see cref="MainWindow"/>).
/// </summary>
public sealed class App : Application
{
    public override void Initialize()
    {
        // Fluent supplies the control templates (buttons, scrollbars, hover/press states); the Nord palette
        // supplies the colours. Pin dark so the Fluent-drawn parts match the Polar Night surface.
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();

            // Decide what (if anything) to surface *before* stamping the current version, then record that
            // we've now run this version so the popup fires once per update, not every launch.
            var pending = ResolvePendingChangelog(settings);
            if (settings.LastSeenVersion != AppInfo.Version)
            {
                settings.LastSeenVersion = AppInfo.Version;
                settings.Save();
            }

            var main = new MainWindow();
            desktop.MainWindow = main;

            if (pending is { Count: > 0 })
            {
                // Wait for the main window to open so the popup can centre on it and take a real owner.
                main.Opened += (_, _) => ShowChangelog(main, pending, settings);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The changelog sections to surface on this launch: nothing unless the feature is on, we have a prior
    // version on record, and it differs from the current one — then only the sections in between.
    private static IReadOnlyList<ChangelogSection>? ResolvePendingChangelog(AppSettings settings)
    {
        if (!settings.ShowChangelogOnUpdate) return null;
        if (string.IsNullOrWhiteSpace(settings.LastSeenVersion)) return null; // fresh install — nothing to show
        if (settings.LastSeenVersion == AppInfo.Version) return null;         // same version — no update
        var markdown = ChangelogMarkdown.LoadEmbedded();
        if (markdown is null) return null;
        var sections = ChangelogParser.UnseenSince(markdown, settings.LastSeenVersion, AppInfo.Version);
        return sections.Count > 0 ? sections : null;
    }

    private static void ShowChangelog(MainWindow owner, IReadOnlyList<ChangelogSection> sections, AppSettings settings)
    {
        var window = ChangelogWindow.ForUpdate(sections, onSuppress: () =>
        {
            settings.ShowChangelogOnUpdate = false;
            settings.Save();
        });
        window.Show(owner);
        window.Activate();
    }
}
