using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Binoc.App.Windows;

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
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
