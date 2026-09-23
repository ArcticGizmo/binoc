using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Binoc.App.History;
using Binoc.App.Theming;
using Binoc.Core.Analysis;
using Binoc.Core.Model;

namespace Binoc.App.Windows;

/// <summary>
/// binoc's one and only window. Two states in one surface: an empty <b>drop zone</b> (choose or drag in a
/// supported file), and the <b>report</b> that replaces it once a file is analysed. Built in code, painted
/// from the fixed Nord <see cref="Palette"/>.
/// </summary>
internal sealed class MainWindow : Window
{
    // Supported inputs, offered in the picker and accepted on drop.
    private static readonly FilePickerFileType SupportedFiles = new("App binaries (APK, AAB, IPA)")
    {
        Patterns = new[] { "*.apk", "*.aab", "*.ipa" },
    };

    private readonly Panel _contentHost = new();
    private readonly Grid _mainGrid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    private readonly Border _sidebarHost = new();
    private readonly Panel _contentLayer = new();
    private Border _dragOverlay = null!;
    private Border _dropZone = null!;
    private readonly HistoryStore _history = new();

    // Drag/search state.
    private bool _dragActive;
    private string? _currentPath;
    private TextBox? _searchBox;
    private TextBlock? _searchCount;
    private readonly List<(Border Card, string Text)> _reportCards = new();

    public MainWindow()
    {
        Title = "binoc";
        Width = 960;
        Height = 720;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.FormBgBrush;

        // Two columns: a persistent recent-history sidebar (left) and the drop-zone/report content (right),
        // with a full-window drag overlay on top.
        _dragOverlay = BuildDragOverlay();
        Grid.SetColumn(_sidebarHost, 0);
        Grid.SetColumn(_contentLayer, 1);
        _mainGrid.Children.Add(_sidebarHost);
        _mainGrid.Children.Add(_contentLayer);
        _contentHost.Children.Add(_mainGrid);
        _contentHost.Children.Add(_dragOverlay);
        Content = _contentHost;

        RefreshSidebar();
        ShowDropZone();

        // Accept files dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Ctrl+F focuses the in-report search.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _searchBox?.Focus();
                _searchBox?.SelectAll();
                e.Handled = true;
            }
        };
    }

    // A full-window "drop here" overlay shown while a file is dragged over the window — in both the empty and
    // report states, so a drop onto a report gets the same visual cue.
    private static Border BuildDragOverlay() => new()
    {
        IsVisible = false,
        IsHitTestVisible = false,
        Background = new SolidColorBrush(Palette.FormBg, 0.82),
        Margin = new Thickness(12),
        BorderBrush = Palette.AccentBrush,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(12),
        Child = new TextBlock
        {
            Text = "Drop to analyse", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Palette.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // ── Empty state: the drop zone ──────────────────────────────────────────────────
    private void ShowDropZone()
    {
        var heading = new TextBlock
        {
            Text = "binoc", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Palette.TitleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var subtitle = new TextBlock
        {
            Text = "Inspect an Android or iOS app binary", FontSize = 14, Foreground = Palette.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 28),
        };

        var prompt = new TextBlock
        {
            Text = "Drop an APK, AAB or IPA here", FontSize = 16, Foreground = Palette.FgBrush,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
        };
        var or = new TextBlock
        {
            Text = "or", FontSize = 13, Foreground = Palette.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12),
        };

        var choose = BinocUi.FlatButton("Choose file…");
        choose.HorizontalAlignment = HorizontalAlignment.Center;
        choose.Background = Palette.AccentBrush;
        choose.Foreground = new SolidColorBrush(Palette.FormBg);
        choose.BorderThickness = new Thickness(0);
        choose.Click += async (_, _) => await ChooseFileAsync();

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { heading, subtitle, prompt, or, choose },
        };

        // A sunken panel that reads as a target, inset from the window edges. Its border lights up in the
        // accent colour while a file is dragged over the window.
        _dropZone = new Border
        {
            Background = Palette.SunkenBrush,
            BorderBrush = Palette.BorderBrush, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12), Margin = new Thickness(24),
            Child = stack,
        };

        _contentLayer.Children.Clear();
        _contentLayer.Children.Add(_dropZone);
    }

    // ── Recent-history sidebar (left column) ─────────────────────────────────────────
    // Rebuilds the sidebar from persisted history and collapses the column when there's nothing to show.
    private void RefreshSidebar()
    {
        var entries = _history.Load();
        if (entries.Count == 0)
        {
            _sidebarHost.IsVisible = false;
            _sidebarHost.Child = null;
            return;
        }

        var list = new StackPanel { Margin = new Thickness(12, 16, 12, 16) };
        list.Children.Add(new TextBlock
        {
            Text = "RECENT", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush,
            Margin = new Thickness(2, 0, 0, 8),
        });

        foreach (var e in entries)
        {
            bool exists = e.Exists;
            bool active = string.Equals(e.FilePath, _currentPath, StringComparison.OrdinalIgnoreCase);

            var line1 = new TextBlock
            {
                Text = e.FileName, FontSize = 13, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = exists ? Palette.FgBrush : Palette.MutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var meta = FormatShort(e.Format)
                + (e.VersionName is { Length: > 0 } v ? $"  ·  {v}" : "")
                + (exists ? "" : "  ·  moved");
            var line2 = new TextBlock
            {
                Text = meta, FontSize = 11, Foreground = Palette.MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis,
            };

            // Delete (✕) removes the entry from history; its Click is handled before the row's tap, so it
            // doesn't also re-open the file.
            var delPath = e.FilePath;
            var del = new Button
            {
                Content = "✕", FontSize = 12, Foreground = Palette.MutedBrush, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(del, "Remove from recent");
            del.Click += (_, _) => { _history.Remove(delPath); RefreshSidebar(); };

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var textStack = new StackPanel { Children = { line1, line2 }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(del, 1);
            rowGrid.Children.Add(textStack);
            rowGrid.Children.Add(del);

            var item = new Border
            {
                Background = active ? Palette.ButtonBgBrush : Palette.SunkenBrush,
                // Border shows only for the active item; inactive borders match the fill so they read as flat.
                BorderBrush = active ? Palette.AccentBrush : Palette.SunkenBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 0, 6),
                Cursor = new Avalonia.Input.Cursor(exists ? StandardCursorType.Hand : StandardCursorType.No),
                Child = rowGrid,
                Opacity = exists ? 1.0 : 0.55,
            };
            if (exists)
            {
                var path = e.FilePath;
                item.PointerPressed += (_, _) => Analyse(path);
            }
            list.Children.Add(item);
        }

        _sidebarHost.Background = Palette.FormBgBrush;
        _sidebarHost.BorderBrush = Palette.BorderBrush;
        _sidebarHost.BorderThickness = new Thickness(0, 0, 1, 0);
        _sidebarHost.Width = 250;
        _sidebarHost.Child = new ScrollViewer
        {
            Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _sidebarHost.IsVisible = true;
    }

    // ── Report state ────────────────────────────────────────────────────────────────
    private void ShowReport(AnalysisReport report)
    {
        _reportCards.Clear();
        var cards = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };

        // Header row: title + "analyse another".
        var another = BinocUi.FlatButton("Analyse another…");
        another.Click += async (_, _) => await ChooseFileAsync();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var titleBlock = new StackPanel();
        titleBlock.Children.Add(new TextBlock
        {
            Text = report.FileName, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Palette.TitleBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = $"{FormatLabel(report.Format)}  ·  {HumanSize(report.FileSizeBytes)}",
            FontSize = 13, Foreground = Palette.MutedBrush, Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(another, 1);
        another.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(titleBlock);
        header.Children.Add(another);
        cards.Children.Add(header);

        // Search bar (Ctrl+F focuses it) — filters the cards below to those that match.
        cards.Children.Add(BuildSearchBar());

        // Each category becomes a filterable card; we remember its lower-cased text for search.
        void AddCard(Control content)
        {
            var card = BinocUi.Card(content);
            _reportCards.Add((card, Normalize(CollectText(content))));
            cards.Children.Add(card);
        }

        AddCard(BuildSummary(report));
        if (report.Identity is { } identity) AddCard(BuildIdentity(identity));
        if (report.Signing is { } signing) AddCard(BuildSigning(signing));
        if (report.Provisioning is { } prov) AddCard(BuildProvisioning(prov));
        if (report.SecurityPosture is { } posture) AddCard(BuildPosture(posture));
        if (report.Code is { } code) AddCard(BuildCode(code));
        if (report.Obfuscation is { } obf) AddCard(BuildObfuscation(obf));
        if (report.NativeLibs is { } native) AddCard(BuildNativeLibs(native));
        if (report.MachO is { } macho) AddCard(BuildMachO(macho));
        if (report.BundleSize is { } bundle) AddCard(BuildBundleSize(bundle));
        if (report.Archive is { } archive) AddCard(BuildArchive(archive));
        if (report.Notes.Count > 0) AddCard(BuildNotes(report));

        var scroll = new ScrollViewer
        {
            Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _contentLayer.Children.Clear();
        _contentLayer.Children.Add(scroll);
    }

    // The in-report search bar. Typing filters cards to those whose text contains the query.
    private Control BuildSearchBar()
    {
        _searchBox = new TextBox
        {
            PlaceholderText = "Search this report…  (Ctrl+F)",
            Background = Palette.SunkenBrush, Foreground = Palette.FgBrush,
            BorderBrush = Palette.BorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), FontSize = 13, Padding = new Thickness(10, 6),
        };
        _searchBox.TextChanged += (_, _) => ApplyFilter(_searchBox!.Text ?? "");

        _searchCount = new TextBlock
        {
            Text = "", FontSize = 12, Foreground = Palette.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetColumn(_searchBox, 0);
        Grid.SetColumn(_searchCount, 1);
        grid.Children.Add(_searchBox);
        grid.Children.Add(_searchCount);
        return grid;
    }

    private void ApplyFilter(string query)
    {
        // Match on whitespace-stripped, lower-cased text so "16kb" finds "16 KB", "arm64" finds "arm64-v8a", etc.
        var q = Normalize(query);
        if (q.Length == 0)
        {
            foreach (var (card, _) in _reportCards) card.IsVisible = true;
            if (_searchCount is not null) _searchCount.Text = "";
            return;
        }

        int matches = 0;
        foreach (var (card, text) in _reportCards)
        {
            bool hit = text.Contains(q, StringComparison.Ordinal);
            card.IsVisible = hit;
            if (hit) matches++;
        }
        if (_searchCount is not null)
            _searchCount.Text = matches == 1 ? "1 section" : $"{matches} sections";
    }

    // Lower-cases and drops all whitespace, so search is forgiving of spacing ("16 KB" ≡ "16kb").
    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // Recursively collects the visible text of a built card, for search matching.
    private static string CollectText(Control control)
    {
        var sb = new System.Text.StringBuilder();
        Walk(control, sb);
        return sb.ToString();

        static void Walk(Control c, System.Text.StringBuilder sb)
        {
            switch (c)
            {
                case TextBlock tb when !string.IsNullOrEmpty(tb.Text): sb.Append(tb.Text).Append(' '); break;
                case TextBox box when !string.IsNullOrEmpty(box.Text): sb.Append(box.Text).Append(' '); break;
            }
            if (c is Panel p)
                foreach (var child in p.Children) Walk(child, sb);
            else if (c is Border b && b.Child is { } bc) Walk(bc, sb);
            else if (c is ContentControl cc && cc.Content is Control ccc) Walk(ccc, sb);
            else if (c is Decorator d && d.Child is { } dc) Walk(dc, sb);
        }
    }

    private static Control BuildSummary(AnalysisReport report)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowSpacing = 8, ColumnSpacing = 16,
        };

        void Row(string label, string value)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value);
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
        }

        Row("FORMAT", FormatLabel(report.Format));
        Row("SIZE", HumanSize(report.FileSizeBytes));
        Row("PATH", report.FilePath);

        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Summary"));
        stack.Children.Add(grid);
        return stack;
    }

    private static Control BuildIdentity(IdentityInfo id)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        void Row(string label, string? value, IBrush? valueBrush = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label); l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value);
            if (valueBrush is not null) v.Foreground = valueBrush;
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
        }

        Row("PACKAGE", id.PackageId);
        var version = id.VersionName is { } vn
            ? (id.VersionCode is { } vc ? $"{vn}  (build {vc})" : vn)
            : id.VersionCode is { } vcOnly ? $"build {vcOnly}" : null;
        Row("VERSION", version);
        Row("MIN SDK", id.MinSdk);
        Row("TARGET SDK", id.TargetSdk);
        Row("COMPILE SDK", id.CompileSdk);
        Row("MIN iOS", id.MinimumOsVersion);
        Row("DEVICE FAMILY", id.DeviceFamily);
        Row("BUILD", id.BuildProvenance);
        if (id.IsDebuggable is { } dbg)
            Row("BUILD TYPE", dbg ? "Debuggable" : "Release (not debuggable)",
                dbg ? Palette.WarnBrush : Palette.OkBrush);

        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Identity"));
        stack.Children.Add(grid);
        return stack;
    }

    private static Control BuildSigning(SigningInfo s)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Signing"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        void Row(string label, string? value, IBrush? brush = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label); l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value); if (brush is not null) v.Foreground = brush;
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
        }

        Row("SCHEMES", s.Schemes.Count > 0 ? string.Join(", ", s.Schemes) : "none detected");
        if (s.Certificate is { } c)
        {
            Row("SUBJECT", c.Subject);
            if (!c.SelfSigned) Row("ISSUER", c.Issuer);
            Row("SHA-256", c.Sha256Fingerprint);
            Row("SHA-1", c.Sha1Fingerprint);
            Row("KEY", $"{c.SignatureAlgorithm}, {c.KeySizeBits}-bit");
            var validBrush = c.IsExpiredOrNotYetValid ? Palette.ErrorBrush : Palette.OkBrush;
            var validRange = $"{c.NotBefore:yyyy-MM-dd} – {c.NotAfter:yyyy-MM-dd}";
            if (!c.IsExpiredOrNotYetValid)
            {
                int daysLeft = (int)Math.Floor((c.NotAfter - DateTimeOffset.UtcNow).TotalDays);
                validRange += daysLeft == 1 ? "  (1 day left)" : $"  ({daysLeft:N0} days left)";
            }
            Row("VALID", validRange, validBrush);
            if (c.IsAndroidDebugKey) Row("KEY TYPE", "Android debug key", Palette.WarnBrush);
            else if (c.SelfSigned) Row("KEY TYPE", "Self-signed", Palette.MutedBrush);
        }
        stack.Children.Add(grid);

        if (s.UploadKeyNotDistribution)
        {
            stack.Children.Add(BinocUi.Separator());
            stack.Children.Add(new TextBlock
            {
                Text = "This is the upload key, not the distribution key. Play App Signing re-signs what ships.",
                FontSize = 12, Foreground = Palette.WarnBrush, TextWrapping = TextWrapping.Wrap,
            });
        }
        return stack;
    }

    private static Control BuildPosture(SecurityPostureInfo p)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Security posture"));

        // Android: permissions — full names, alphabetical, as a bulleted list (easier to read the namespace
        // at a glance than truncated chips). Dangerous/high-risk ones are coloured.
        if (p.Permissions.Count > 0)
        {
            int dangerous = p.DangerousPermissions.Count();
            stack.Children.Add(new TextBlock
            {
                Text = $"PERMISSIONS ({p.Permissions.Count}, {dangerous} high-risk)",
                FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush,
                Margin = new Thickness(0, 0, 0, 6),
            });
            foreach (var perm in p.Permissions.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 2) };
                row.Children.Add(new TextBlock
                {
                    Text = "•", FontSize = 13, Foreground = perm.Dangerous ? Palette.ErrorBrush : Palette.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                var name = BinocUi.SelectableText(perm.Name + (perm.Dangerous ? "  (high-risk)" : ""));
                name.FontSize = 12;
                name.Foreground = perm.Dangerous ? Palette.ErrorBrush : Palette.FgBrush;
                row.Children.Add(name);
                stack.Children.Add(row);
            }
        }

        if (p.ExportedComponents.Count > 0)
        {
            stack.Children.Add(PostureLabel($"EXPORTED COMPONENTS ({p.ExportedComponents.Count})"));
            foreach (var c in p.ExportedComponents)
                stack.Children.Add(new TextBlock
                {
                    Text = $"{c.Type}: {c.Name}", FontSize = 12, Foreground = Palette.FgBrush, TextWrapping = TextWrapping.Wrap,
                });
        }

        if (p.UsesCleartextTraffic is { } ct)
            stack.Children.Add(PostureLine("Cleartext traffic", ct ? "allowed (HTTP permitted)" : "not allowed",
                ct ? Palette.WarnBrush : Palette.OkBrush));

        // iOS: ATS, URL schemes, usage descriptions, privacy manifest.
        if (p.AtsAllowsArbitraryLoads is { } ats)
            stack.Children.Add(PostureLine("App Transport Security",
                ats ? "arbitrary loads allowed (HTTP permitted)" : "enforced", ats ? Palette.WarnBrush : Palette.OkBrush));
        if (p.AtsExceptionDomains.Count > 0)
            stack.Children.Add(PostureLine("ATS exception domains", string.Join(", ", p.AtsExceptionDomains), Palette.WarnBrush));
        if (p.HasPrivacyManifest is { } pm)
            stack.Children.Add(PostureLine("Privacy manifest", pm ? "present" : "absent",
                pm ? Palette.OkBrush : Palette.MutedBrush));
        if (p.UrlSchemes.Count > 0)
            stack.Children.Add(PostureLine("URL schemes", string.Join(", ", p.UrlSchemes), Palette.FgBrush));
        if (p.UsageDescriptions.Count > 0)
        {
            stack.Children.Add(PostureLabel($"PRIVACY PROMPTS ({p.UsageDescriptions.Count})"));
            foreach (var u in p.UsageDescriptions)
                stack.Children.Add(new TextBlock
                {
                    Text = $"{u.Key.Replace("UsageDescription", "")}: {u.Purpose}",
                    FontSize = 12, Foreground = Palette.MutedBrush, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2),
                });
        }

        // Cross-format: potential secrets (kind + location only, never the value).
        if (p.PotentialSecrets.Count > 0)
        {
            stack.Children.Add(PostureLabel($"POTENTIAL SECRETS ({p.PotentialSecrets.Count}) — heuristic"));
            foreach (var s in p.PotentialSecrets)
                stack.Children.Add(new TextBlock
                {
                    Text = $"{s.Kind} in {s.File}", FontSize = 12, Foreground = Palette.WarnBrush, TextWrapping = TextWrapping.Wrap,
                });
        }

        return stack;
    }

    private static TextBlock PostureLabel(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static Control PostureLine(string label, string value, IBrush valueBrush)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = valueBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    // A posture line for an enabled/disabled/unknown flag.
    private static Control FlagLine(string label, bool? on) => PostureLine(label,
        on switch { true => "enabled", false => "disabled", _ => "unknown" },
        on == true ? Palette.OkBrush : Palette.MutedBrush);

    // A metric line: label + whole-number percentage, coloured against Play's 25% floor.
    private static Control MetricLine(string label, int percent)
    {
        var brush = percent >= 60 ? Palette.OkBrush : percent >= 25 ? Palette.WarnBrush : Palette.ErrorBrush;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush, VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 });
        row.Children.Add(new TextBlock { Text = $"{percent}%", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = brush, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static Control BuildProvisioning(ProvisioningInfo p)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        void Row(string label, string? value, IBrush? brush = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label); l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value); if (brush is not null) v.Foreground = brush;
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
        }

        Row("TYPE", p.DistributionType, Palette.AccentBrush);
        Row("PROFILE", p.Name);
        Row("TEAM", p.TeamName is { } t && p.TeamId is { } id ? $"{t} ({id})" : p.TeamName ?? p.TeamId);
        Row("APP ID", p.AppId);
        if (p.GetTaskAllow is { } gta) Row("GET-TASK-ALLOW", gta ? "true (debuggable)" : "false",
            gta ? Palette.WarnBrush : Palette.OkBrush);
        if (p.ProvisionedDeviceCount > 0) Row("DEVICES", $"{p.ProvisionedDeviceCount:N0} provisioned");
        if (p.ExpirationDate is { } exp)
            Row("EXPIRES", exp.ToString("yyyy-MM-dd"), exp < DateTimeOffset.UtcNow ? Palette.ErrorBrush : Palette.FgBrush);
        if (p.Entitlements.Count > 0) Row("ENTITLEMENTS", string.Join(", ", p.Entitlements));

        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Provisioning"));
        stack.Children.Add(grid);
        return stack;
    }

    private static Control BuildCode(CodeInfo code)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        void Row(string label, string value, IBrush? brush = null)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label); l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value); if (brush is not null) v.Foreground = brush;
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
        }

        Row("DEX FILES", code.MultiDex ? $"{code.DexFileCount} (multidex)" : code.DexFileCount.ToString());
        Row("METHOD REFS", $"{code.TotalMethodRefs:N0} total");
        Row("MAX PER DEX", $"{code.MaxMethodRefsInADex:N0} / 65,536",
            code.NearMethodLimit ? Palette.WarnBrush : Palette.FgBrush);
        Row("CLASSES", $"{code.TotalDefinedClasses:N0}");

        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Code (DEX)"));
        stack.Children.Add(grid);
        return stack;
    }

    private static Control BuildObfuscation(ObfuscationInfo o)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Optimisation & obfuscation"));

        // The three Play metrics — read verbatim from r8.json, or a clear "missing" state (never guessed).
        if (o.HasMetrics)
        {
            if (o.ObfuscationPercent is { } obf) stack.Children.Add(MetricLine("Obfuscation", obf));
            if (o.OptimizationPercent is { } opt) stack.Children.Add(MetricLine("Optimisation", opt));
            if (o.ShrinkingPercent is { } shr) stack.Children.Add(MetricLine("Shrinking", shr));
            stack.Children.Add(new TextBlock
            {
                Text = "Read from r8.json — the exact figures Google Play reports (Play enforces a 25% floor from Feb 2027 "
                    + "for apps with non-negligible DEX).",
                FontSize = 11, Foreground = Palette.MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2),
            });
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No r8.json in this file — not enough information to report optimisation / obfuscation / shrinking. "
                    + "R8 embeds it only in AABs built with AGP 8.10+ / recent R8 (APKs never carry it). binoc doesn't "
                    + "estimate these; analyse the .aab, and make sure R8 is on (minifyEnabled true).",
                FontSize = 12, Foreground = Palette.WarnBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
            });
        }

        // R8 config, when r8.json is present.
        if (o.HasR8Metadata)
        {
            stack.Children.Add(PostureLabel("R8 CONFIG"));
            stack.Children.Add(PostureLine("R8 version", o.R8Version ?? "unknown", Palette.FgBrush));
            stack.Children.Add(FlagLine("Optimisations (full mode)", o.R8OptimizationsEnabled));
            stack.Children.Add(FlagLine("Repackage classes", o.R8RepackageClassesEnabled));
            stack.Children.Add(FlagLine("Optimised resource shrinking", o.R8OptimizedResourceShrinkingEnabled));
        }

        if (o.HasEmbeddedDeobfuscationMap is { } hasMap)
            stack.Children.Add(PostureLine("Deobfuscation map",
                hasMap ? "embedded in bundle (Play uses it to de-obfuscate crashes)" : "not embedded",
                hasMap ? Palette.OkBrush : Palette.MutedBrush));

        // Packer / protector — concrete signature matches, not a score.
        if (o.Signals.Count > 0)
        {
            stack.Children.Add(PostureLabel("PACKER / PROTECTOR"));
            foreach (var s in o.Signals)
                stack.Children.Add(new TextBlock
                {
                    Text = $"• {s.Name} — {s.Detail}", FontSize = 12, Foreground = Palette.ErrorBrush,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2),
                });
        }

        // Raw r8.json, collapsed by default — the exact metadata Play reads, copyable.
        if (o.R8MetadataRaw is { Length: > 0 } raw)
        {
            var json = BinocUi.SelectableText(raw);
            json.FontFamily = new FontFamily("Consolas, Menlo, monospace");
            json.FontSize = 12;
            stack.Children.Add(new Expander
            {
                Header = "View r8.json",
                Foreground = Palette.FgBrush,
                Margin = new Thickness(0, 10, 0, 0),
                Content = new ScrollViewer
                {
                    MaxHeight = 320, Content = json,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            });
        }

        return stack;
    }

    private static Control BuildBundleSize(BundleSizeInfo b)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Per-device download size (estimate)"));

        var dims = b.SplitDimensions.Count > 0 ? string.Join(", ", b.SplitDimensions) : "none";
        stack.Children.Add(new TextBlock
        {
            Text = $"Splits by: {dims}" + (b.DimensionsFromConfig ? "  (from BundleConfig.pb)" : "  (bundletool defaults assumed)"),
            FontSize = 12, Foreground = Palette.MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });

        // Device estimate table: profile · abi/density/lang · download · saving.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            RowSpacing = 6, ColumnSpacing = 16,
        };
        void Cell(int r, int c, string text, IBrush brush, bool header = false)
        {
            var t = new TextBlock
            {
                Text = text, FontSize = header ? 11 : 13, Foreground = brush,
                FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            Grid.SetRow(t, r); Grid.SetColumn(t, c);
            grid.Children.Add(t);
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(0, 0, "DEVICE", Palette.MutedBrush, header: true);
        Cell(0, 1, "CONFIG", Palette.MutedBrush, header: true);
        Cell(0, 2, "DOWNLOAD", Palette.MutedBrush, header: true);
        Cell(0, 3, "SAVING", Palette.MutedBrush, header: true);

        int row = 1;
        foreach (var d in b.Devices)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(row, 0, d.Profile, Palette.FgBrush);
            Cell(row, 1, $"{d.Abi} · {d.Density} · {d.Language}", Palette.MutedBrush);
            Cell(row, 2, HumanSize(d.DownloadBytes), Palette.FgBrush);
            Cell(row, 3, $"−{d.SavingsFraction:P0}", d.SavingsFraction > 0.01 ? Palette.OkBrush : Palette.MutedBrush);
            row++;
        }
        stack.Children.Add(grid);

        stack.Children.Add(BinocUi.Separator());
        stack.Children.Add(new TextBlock
        {
            Text = $"Universal APK (all ABIs/densities/languages): {HumanSize(b.UniversalApkBytes)}",
            FontSize = 12, Foreground = Palette.FgBrush, TextWrapping = TextWrapping.Wrap,
        });

        // The dimension breakdowns, compact.
        if (b.Abis.Count > 0)
            stack.Children.Add(DimensionLine("ABIs", b.Abis));
        if (b.Densities.Count > 0)
            stack.Children.Add(DimensionLine("Densities", b.Densities));
        if (b.Languages.Count > 0)
            stack.Children.Add(DimensionLine("Languages", b.Languages));

        stack.Children.Add(new TextBlock
        {
            Text = "Estimate from compressed entry sizes — binoc doesn't run bundletool; Play's actual splits may differ.",
            FontSize = 11, Foreground = Palette.MutedBrush, FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        return stack;
    }

    private static Control DimensionLine(string label, System.Collections.Generic.List<DimensionSplit> splits)
    {
        var text = string.Join(", ", splits.Select(s => $"{s.Name} ({HumanSize(s.Bytes)})"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush, VerticalAlignment = VerticalAlignment.Top });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Palette.FgBrush, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    private static Control BuildNativeLibs(NativeLibsInfo n)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Native libraries"));
        stack.Children.Add(new TextBlock
        {
            Text = $"ABIs: {string.Join(", ", n.Abis)}", FontSize = 13, Foreground = Palette.FgBrush,
            Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
        });

        // 16 KB page-size support (Android 15+): the whole-app verdict, then a per-binary chip below.
        stack.Children.Add(PostureLine("16 KB page size",
            n.AllSupport16kPages ? "supported (all libraries aligned ≥16 KB)" : "not fully supported (some built for 4 KB pages)",
            n.AllSupport16kPages ? Palette.OkBrush : Palette.WarnBrush));

        stack.Children.Add(new Border { Height = 8 });

        // One row per binary: name + checksec chips.
        foreach (var b in n.Binaries)
        {
            var name = new TextBlock
            {
                Text = $"{b.Abi} · {System.IO.Path.GetFileName(b.Path)}  ({b.Arch})",
                FontSize = 12, Foreground = Palette.MutedBrush, Margin = new Thickness(0, 0, 0, 3),
            };
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            chips.Children.Add(Chip("NX", b.Nx));
            chips.Children.Add(Chip($"RELRO:{b.Relro}", b.Relro == "full", b.Relro == "partial"));
            chips.Children.Add(Chip("canary", b.StackCanary));
            chips.Children.Add(Chip(b.Stripped ? "stripped" : "unstripped", b.Stripped));
            chips.Children.Add(Chip("PIE", b.Pie));
            chips.Children.Add(Chip(b.Supports16kPages ? "16 KB" : "4 KB only", b.Supports16kPages));
            stack.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 10), Children = { name, chips } });
        }
        return stack;
    }

    private static Control BuildMachO(MachOInfo m)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Mach-O executable"));
        stack.Children.Add(new TextBlock
        {
            Text = m.IsFat ? $"{m.Executable} — universal ({m.Architectures.Count} slices)" : $"{m.Executable} — thin",
            FontSize = 13, Foreground = Palette.FgBrush, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
        });

        foreach (var a in m.Architectures)
        {
            var name = new TextBlock { Text = a.Arch, FontSize = 12, Foreground = Palette.MutedBrush, Margin = new Thickness(0, 0, 0, 3) };
            var chips = new WrapPanel();
            chips.Children.Add(Chip("PIE", a.Pie));
            // Encrypted is expected/good for a store binary; show it neutrally as present.
            chips.Children.Add(Chip(a.Encrypted ? "encrypted" : "not encrypted", a.Encrypted, partial: !a.Encrypted));
            chips.Children.Add(Chip("code sig", a.HasCodeSignature));
            chips.Children.Add(Chip("canary", a.StackCanary));
            stack.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 10), Children = { name, chips } });
        }

        if (m.LinkedLibraries.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"LINKED ({m.LinkedLibraries.Count})", FontSize = 11, FontWeight = FontWeight.SemiBold,
                Foreground = Palette.MutedBrush, Margin = new Thickness(0, 4, 0, 6),
            });
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("\n", m.LinkedLibraries), FontSize = 12, Foreground = Palette.FgBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return stack;
    }

    // A small posture chip: green when good, yellow when partial, muted-red when weak.
    private static Control Chip(string text, bool good, bool partial = false)
    {
        var brush = good ? Palette.OkBrush : partial ? Palette.WarnBrush : Palette.ErrorBrush;
        return new Border
        {
            Background = Palette.SunkenBrush, CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2), Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = brush },
        };
    }

    private static Control BuildArchive(ArchiveInfo archive)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Archive & size"));

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 16 };
        void Row(string label, string value)
        {
            int r = top.RowDefinitions.Count;
            top.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = BinocUi.FieldLabel(label); l.VerticalAlignment = VerticalAlignment.Center;
            var v = BinocUi.ValueText(value);
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            top.Children.Add(l); top.Children.Add(v);
        }
        Row("ENTRIES", $"{archive.EntryCount:N0}");
        Row("DOWNLOAD", $"{HumanSize(archive.CompressedSize)}  compressed");
        Row("INSTALLED", $"{HumanSize(archive.UncompressedSize)}  uncompressed");
        stack.Children.Add(top);

        // Per-type breakdown: a labelled bar per bucket, sized by share of the compressed total.
        if (archive.Buckets.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "BY TYPE", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Palette.MutedBrush,
                Margin = new Thickness(0, 14, 0, 8),
            });
            long total = Math.Max(1, archive.Buckets.Sum(b => b.CompressedSize));
            foreach (var b in archive.Buckets)
                stack.Children.Add(BuildBucketRow(b, total));
        }

        if (archive.Alignment is { } al)
        {
            stack.Children.Add(BinocUi.Separator());
            string align4 = al.Aligned4 ? "4-byte aligned (zipaligned)" : $"not zipaligned ({al.MisalignedCount} misaligned)";
            var row = new TextBlock { Text = $"Alignment: {align4}", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            row.Foreground = al.Aligned4 ? Palette.OkBrush : Palette.WarnBrush;
            stack.Children.Add(row);
            if (al.NativeLibs16k is { } n16)
                stack.Children.Add(new TextBlock
                {
                    Text = n16 ? "Native libraries: 16 KB-aligned" : "Native libraries: not 16 KB-aligned (Android 15+ devices)",
                    FontSize = 12, Foreground = n16 ? Palette.OkBrush : Palette.WarnBrush, TextWrapping = TextWrapping.Wrap,
                });
        }

        return stack;
    }

    private static Control BuildBucketRow(SizeBucket b, long total)
    {
        double share = (double)b.CompressedSize / total;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var name = new TextBlock { Text = b.Label, FontSize = 13, Foreground = Palette.FgBrush };
        var size = new TextBlock
        {
            Text = $"{HumanSize(b.CompressedSize)}  ·  {b.EntryCount:N0}", FontSize = 12, Foreground = Palette.MutedBrush,
        };
        Grid.SetColumn(name, 0); Grid.SetColumn(size, 1);
        header.Children.Add(name); header.Children.Add(size);

        // A thin proportional bar under the label.
        var track = new Border
        {
            Height = 6, CornerRadius = new CornerRadius(3), Background = Palette.SunkenBrush,
            Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new Border
            {
                Height = 6, CornerRadius = new CornerRadius(3), Background = Palette.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 2,
                Width = double.NaN, Tag = share,
            },
        };
        // Width the fill proportionally once the track has laid out.
        track.LayoutUpdated += (_, _) =>
        {
            if (track.Child is Border fill && track.Bounds.Width > 0)
                fill.Width = Math.Max(2, track.Bounds.Width * share);
        };

        return new StackPanel { Margin = new Thickness(0, 0, 0, 10), Children = { header, track } };
    }

    private static Control BuildNotes(AnalysisReport report)
    {
        var stack = new StackPanel();
        stack.Children.Add(BinocUi.SectionTitle("Notes"));

        foreach (var note in report.Notes)
        {
            // A Grid (not a horizontal StackPanel): the '*' text column is bounded by the card width, so the
            // message actually wraps instead of running off the edge.
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 6),
            };
            var bar = new Border
            {
                Width = 4, CornerRadius = new CornerRadius(2), Background = BinocUi.SeverityBrush(note.Severity),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var text = BinocUi.SelectableText(note.Message);
            text.FontSize = 13;
            Grid.SetColumn(bar, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(bar);
            row.Children.Add(text);
            stack.Children.Add(row);
        }
        return stack;
    }

    // ── Input: picker + drag/drop ─────────────────────────────────────────────────────
    private async Task ChooseFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an APK, AAB or IPA",
            AllowMultiple = false,
            FileTypeFilter = new[] { SupportedFiles },
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrEmpty(path)) Analyse(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Avalonia 12 drag/drop model: inspect the incoming IDataTransfer for the File format.
        bool hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

        // DragOver fires on every pointer move; only touch the visual tree when the state actually changes,
        // otherwise repeated overlay/border invalidations make the drag stutter.
        if (hasFiles == _dragActive) return;
        SetDragActive(hasFiles);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => SetDragActive(false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDragActive(false);
        var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) Analyse(path);
    }

    private void SetDragActive(bool active)
    {
        if (active == _dragActive && _dragOverlay.IsVisible == active) return;
        _dragActive = active;
        _dragOverlay.IsVisible = active;
        if (_dropZone.Parent is not null)
            _dropZone.BorderBrush = active ? Palette.AccentBrush : Palette.BorderBrush;
    }

    private void Analyse(string path)
    {
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            if (report.Format != BinaryFormat.Unknown)
            {
                try { _history.Add(report); } catch { /* history is a convenience, never fatal */ }
            }
            _currentPath = report.FilePath;
            ShowReport(report);
            RefreshSidebar();
        }
        catch (Exception ex)
        {
            // A hard failure before a report even exists (e.g. the file vanished) — surface it in a minimal
            // report rather than crashing.
            var report = new AnalysisReport
            {
                FilePath = path, FileName = Path.GetFileName(path),
                FileSizeBytes = 0, Format = BinaryFormat.Unknown,
            };
            report.Notes.Add(new ReportNote("general", NoteSeverity.Error, $"Couldn't analyse the file: {ex.Message}"));
            ShowReport(report);
        }
    }

    // ── Formatting ─────────────────────────────────────────────────────────────────
    private static string FormatLabel(BinaryFormat format) => format switch
    {
        BinaryFormat.Apk => "Android APK",
        BinaryFormat.Aab => "Android App Bundle (AAB)",
        BinaryFormat.Ipa => "iOS app archive (IPA)",
        _ => "Unrecognised file",
    };

    // Short format tag for the recent-history rows (the stored value is BinaryFormat.ToString()).
    private static string FormatShort(string format) => format switch
    {
        nameof(BinaryFormat.Apk) => "APK",
        nameof(BinaryFormat.Aab) => "AAB",
        nameof(BinaryFormat.Ipa) => "IPA",
        _ => format,
    };

    private static string HumanSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {units[unit]}";
    }
}
