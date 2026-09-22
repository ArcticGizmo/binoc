using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Binoc.App.Theming;
using Binoc.Core.Model;

namespace Binoc.App.Windows;

/// <summary>
/// Small factory helpers for the themed text / buttons / cards the window builds from — binoc's trimmed
/// counterpart of Perch's <c>SettingsUi</c>. Everything reads the fixed Nord <see cref="Palette"/> so the
/// whole surface renders as one app.
/// </summary>
internal static class BinocUi
{
    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold,
        Foreground = Palette.TitleBrush, Margin = new Thickness(0, 4, 0, 8),
    };

    public static TextBlock BodyText(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13,
        Foreground = Palette.MutedBrush, Margin = new Thickness(0, 0, 0, 6),
    };

    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush,
    };

    /// <summary>A read-only but <b>selectable/copyable</b> text block — the report is data people want to
    /// grab (fingerprints, ids, permission names), so value text uses this rather than a plain TextBlock.</summary>
    public static SelectableTextBlock SelectableText(string text) => new()
    {
        Text = text, FontSize = 14, Foreground = Palette.FgBrush, TextWrapping = TextWrapping.Wrap,
        SelectionBrush = new SolidColorBrush(Palette.Accent, 0.35),
    };

    public static SelectableTextBlock ValueText(string text) => SelectableText(text);

    public static Border Separator() => new()
    {
        Height = 1, Background = Palette.BorderBrush, Margin = new Thickness(0, 12, 0, 12),
    };

    /// <summary>A flat accent-outlined button matching the surface (Fluent supplies the hover/press shading).</summary>
    public static Button FlatButton(string text) => new()
    {
        Content = text, Background = Palette.ButtonBgBrush, Foreground = Palette.FgBrush,
        BorderBrush = Palette.BorderBrush, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8), FontSize = 13,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>A card container: a rounded, bordered panel on the button surface, holding a section of the
    /// report. This is the unit the report is built from (one card per populated category).</summary>
    public static Border Card(Control content) => new()
    {
        Background = Palette.ButtonBgBrush, BorderBrush = Palette.BorderBrush, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12), Margin = new Thickness(0, 0, 0, 12),
        Child = content,
    };

    /// <summary>The Aurora hue a note's severity draws in (findings §posture severity chips).</summary>
    public static IBrush SeverityBrush(NoteSeverity severity) => severity switch
    {
        NoteSeverity.Error => Palette.ErrorBrush,
        NoteSeverity.Warning => Palette.WarnBrush,
        _ => Palette.MutedBrush,
    };
}
