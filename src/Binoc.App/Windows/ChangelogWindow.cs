using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Binoc.App.Theming;
using Binoc.Core.Changelog;

namespace Binoc.App.Windows;

/// <summary>
/// The "what's new" window: a headline, a scrollable list of changelog sections, and a Close button. Serves
/// two callers — the post-update popup (a range of sections since the last-seen version, with a "Don't show
/// changelogs again" button that suppresses future pop-ups via <paramref name="onSuppress"/>), and the
/// manual "Changelog" view opened from the window (every section, no suppress button). Styled off binoc's
/// Nord <see cref="Palette"/> so it reads as one app. Adapted from Perch's ChangelogWindow.
/// </summary>
internal sealed class ChangelogWindow : Window
{
    private readonly Action? _onSuppress;

    public ChangelogWindow(string headline, string subhead, IReadOnlyList<ChangelogSection> sections, Action? onSuppress = null)
    {
        _onSuppress = onSuppress;

        Title = headline;
        Icon = LoadAppIcon();
        Width = 500;
        Height = 620;
        MinWidth = 380;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.FormBgBrush;

        Content = BuildContent(headline, subhead, sections);
    }

    private Control BuildContent(string headline, string subhead, IReadOnlyList<ChangelogSection> sections)
    {
        var title = new TextBlock
        {
            Text = headline, Foreground = Palette.TitleBrush, FontWeight = FontWeight.Bold, FontSize = 18,
        };
        var sub = new TextBlock
        {
            Text = subhead, Foreground = Palette.MutedBrush, FontSize = 12, Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var header = new StackPanel { Children = { title, sub }, Margin = new Thickness(0, 0, 0, 12) };

        var body = new StackPanel();
        if (sections.Count == 0)
            body.Children.Add(BinocUi.BodyText("No changelog entries in that range."));
        for (int i = 0; i < sections.Count; i++)
        {
            if (i > 0) body.Children.Add(BinocUi.Separator());
            ChangelogMarkdown.Render(body, sections[i].Block);
        }

        var scroller = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
        };

        // The suppress button only makes sense for the post-update popup; the manual view passes no callback.
        if (_onSuppress is not null)
        {
            var suppress = BinocUi.FlatButton("Don't show changelogs again");
            suppress.Click += (_, _) => { try { _onSuppress(); } catch { /* best-effort */ } Close(); };
            buttons.Children.Add(suppress);
        }

        var close = BinocUi.FlatButton("Close");
        close.Background = Palette.AccentBrush;
        close.Foreground = new SolidColorBrush(Palette.FormBg);
        close.BorderThickness = new Thickness(0);
        close.MinWidth = 84;
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22) };
        Grid.SetRow(header, 0);
        Grid.SetRow(scroller, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(header);
        grid.Children.Add(scroller);
        grid.Children.Add(buttons);
        return grid;
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://binoc/Assets/binoc.ico"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    // ── Factory helpers ─────────────────────────────────────────────────────────────

    /// <summary>The post-update popup: the sections released since <c>lastSeen</c>, with a suppress button.</summary>
    public static ChangelogWindow ForUpdate(IReadOnlyList<ChangelogSection> sections, Action onSuppress)
    {
        string subhead = sections.Count == 1
            ? $"Updated to {sections[0].Display}."
            : $"Updated to {sections[0].Display} — {sections.Count} releases since {sections[^1].Display}.";
        return new ChangelogWindow("What's new in binoc", subhead, sections, onSuppress);
    }

    /// <summary>The manual "Changelog" view: every documented version, no suppress button.</summary>
    public static ChangelogWindow FullHistory()
    {
        var markdown = ChangelogMarkdown.LoadEmbedded() ?? "";
        var sections = ChangelogParser.Parse(markdown)
            .Where(s => s.Version is not null)   // skip the empty [Unreleased] section
            .ToList();
        return new ChangelogWindow("binoc changelog", $"Currently running {DisplayVersion()}.", sections);
    }

    private static string DisplayVersion()
    {
        var v = AppInfo.Version;
        return v.StartsWith('v') ? v : "v" + v;
    }
}
