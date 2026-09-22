using Avalonia.Media;

namespace Binoc.App.Theming;

/// <summary>
/// binoc's single, fixed colour palette: <b>Nord (dark)</b>. Unlike Perch — which resolves colours through a
/// swappable <c>Theme</c> model and a theme designer — binoc pins one theme, so this is a flat set of Nord
/// roles exposed as both <see cref="Color"/> (for owner-draw) and cached <see cref="SolidColorBrush"/> fills.
/// The member names mirror Perch's <c>Palette</c> vocabulary so its UI-helper idioms port cleanly.
///
/// <para>Reference: the official Nord palette — Polar Night (nord0–3), Snow Storm (nord4–6), Frost
/// (nord7–10), Aurora (nord11–15). https://www.nordtheme.com/ </para>
/// </summary>
public static class Palette
{
    // ── Nord source colours ────────────────────────────────────────────────────────
    // Polar Night
    private static readonly Color Nord0 = Color.Parse("#2E3440");
    private static readonly Color Nord1 = Color.Parse("#3B4252");
    private static readonly Color Nord2 = Color.Parse("#434C5E");
    private static readonly Color Nord3 = Color.Parse("#4C566A");
    // Snow Storm
    private static readonly Color Nord4 = Color.Parse("#D8DEE9");
    private static readonly Color Nord6 = Color.Parse("#ECEFF4");
    // Frost
    private static readonly Color Nord8 = Color.Parse("#88C0D0");
    private static readonly Color Nord9 = Color.Parse("#81A1C1");
    private static readonly Color Nord10 = Color.Parse("#5E81AC");
    // Aurora
    private static readonly Color Nord11 = Color.Parse("#BF616A"); // red
    private static readonly Color Nord12 = Color.Parse("#D08770"); // orange
    private static readonly Color Nord13 = Color.Parse("#EBCB8B"); // yellow
    private static readonly Color Nord14 = Color.Parse("#A3BE8C"); // green
    // A readable muted slate (Nord's "comment" tone), for secondary text on the dark surface.
    private static readonly Color NordMuted = Color.Parse("#7B88A1");

    // ── Semantic roles (Color) ──────────────────────────────────────────────────────
    public static Color FormBg => Nord0;       // window background
    public static Color Sunken => Nord1;        // rails, drop zone, sunken panels
    public static Color ButtonBg => Nord2;      // buttons, cards, inputs
    public static Color Border => Nord3;        // borders, separators
    public static Color Fg => Nord4;            // body text
    public static Color Title => Nord6;         // headings
    public static Color Muted => NordMuted;     // secondary/caption text
    public static Color Accent => Nord8;        // primary accent (Frost cyan)
    public static Color AccentDeep => Nord10;   // pressed/secondary accent
    public static Color AccentAlt => Nord9;

    // Posture / status hues (findings §security-posture severity chips).
    public static Color Error => Nord11;
    public static Color Warn => Nord13;
    public static Color Ok => Nord14;
    public static Color Info => Nord12;

    // ── Cached brushes ──────────────────────────────────────────────────────────────
    public static readonly SolidColorBrush FormBgBrush = new(FormBg);
    public static readonly SolidColorBrush SunkenBrush = new(Sunken);
    public static readonly SolidColorBrush ButtonBgBrush = new(ButtonBg);
    public static readonly SolidColorBrush BorderBrush = new(Border);
    public static readonly SolidColorBrush FgBrush = new(Fg);
    public static readonly SolidColorBrush TitleBrush = new(Title);
    public static readonly SolidColorBrush MutedBrush = new(Muted);
    public static readonly SolidColorBrush AccentBrush = new(Accent);
    public static readonly SolidColorBrush AccentDeepBrush = new(AccentDeep);

    public static readonly SolidColorBrush ErrorBrush = new(Error);
    public static readonly SolidColorBrush WarnBrush = new(Warn);
    public static readonly SolidColorBrush OkBrush = new(Ok);
    public static readonly SolidColorBrush InfoBrush = new(Info);
}
