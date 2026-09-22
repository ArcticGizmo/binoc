using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private Border _dropZone = null!;

    public MainWindow()
    {
        Title = "binoc";
        Width = 900;
        Height = 720;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.FormBgBrush;

        Content = _contentHost;
        ShowDropZone();

        // Accept files dropped anywhere on the window.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

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

        _contentHost.Children.Clear();
        _contentHost.Children.Add(_dropZone);
    }

    // ── Report state ────────────────────────────────────────────────────────────────
    private void ShowReport(AnalysisReport report)
    {
        var cards = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };

        // Header row: title + "analyse another".
        var another = BinocUi.FlatButton("Analyse another…");
        another.Click += async (_, _) => await ChooseFileAsync();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 16) };
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

        // Summary card.
        cards.Children.Add(BinocUi.Card(BuildSummary(report)));

        // Identity card.
        if (report.Identity is { } identity)
            cards.Children.Add(BinocUi.Card(BuildIdentity(identity)));

        // Archive / size breakdown card.
        if (report.Archive is { } archive)
            cards.Children.Add(BinocUi.Card(BuildArchive(archive)));

        // Notes card (degradation / context).
        if (report.Notes.Count > 0)
            cards.Children.Add(BinocUi.Card(BuildNotes(report)));

        var scroll = new ScrollViewer
        {
            Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _contentHost.Children.Clear();
        _contentHost.Children.Add(scroll);
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

        if (archive.NewestEntry is { } ts)
        {
            stack.Children.Add(BinocUi.Separator());
            stack.Children.Add(new TextBlock
            {
                Text = $"Newest entry: {ts.Value:yyyy-MM-dd HH:mm} ({ts.Source})",
                FontSize = 12, Foreground = Palette.FgBrush, TextWrapping = TextWrapping.Wrap,
            });
            stack.Children.Add(new TextBlock
            {
                Text = ts.ProvenanceNote, FontSize = 11, Foreground = Palette.MutedBrush,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
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
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new Border
            {
                Width = 4, CornerRadius = new CornerRadius(2), Background = BinocUi.SeverityBrush(note.Severity),
            });
            row.Children.Add(new TextBlock
            {
                Text = note.Message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Palette.FgBrush,
                MaxWidth = 720,
            });
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
        if (hasFiles) _dropZone.BorderBrush = Palette.AccentBrush;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
        => _dropZone.BorderBrush = Palette.BorderBrush;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _dropZone.BorderBrush = Palette.BorderBrush;

        var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) Analyse(path);
    }

    private void Analyse(string path)
    {
        try
        {
            var report = AnalysisPipeline.Analyze(path);
            ShowReport(report);
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
