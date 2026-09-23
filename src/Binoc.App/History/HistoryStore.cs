using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Binoc.Core.Model;

namespace Binoc.App.History;

/// <summary>
/// A small, local-only "recent analyses" list persisted to <c>%APPDATA%\binoc\history.json</c>. It stores just
/// enough to re-open a file and recognise it (path, format, id, version, date, size) — nothing leaves the
/// machine. Most-recent first, de-duplicated by path, capped. All I/O is best-effort: a missing/corrupt file
/// yields an empty list rather than an error, since history is a convenience, never load-bearing.
/// </summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 25;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public HistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "binoc", "history.json");
    }

    /// <summary>Loads the saved entries (most-recent first). Never throws.</summary>
    public List<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<HistoryEntry>();
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path));
            return entries ?? new List<HistoryEntry>();
        }
        catch
        {
            return new List<HistoryEntry>();
        }
    }

    /// <summary>Records a completed analysis and persists it. A file that's already in the list is updated
    /// <b>in place</b> — its position is kept, so re-opening a recent item doesn't reshuffle the list; only a
    /// genuinely new file is added (at the top). Never throws.</summary>
    public List<HistoryEntry> Add(AnalysisReport report)
    {
        var entries = Load();
        var entry = new HistoryEntry
        {
            FilePath = report.FilePath,
            FileName = report.FileName,
            Format = report.Format.ToString(),
            PackageId = report.Identity?.PackageId,
            VersionName = report.Identity?.VersionName,
            VersionCode = report.Identity?.VersionCode,
            FileSizeBytes = report.FileSizeBytes,
            AnalyzedAtUtc = System.DateTimeOffset.UtcNow,
        };

        int idx = entries.FindIndex(e =>
            string.Equals(e.FilePath, report.FilePath, System.StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            entries[idx] = entry; // keep its slot in the list
        }
        else
        {
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Save(entries);
        return entries;
    }

    /// <summary>Removes the entry for a path (if present) and persists. Never throws.</summary>
    public List<HistoryEntry> Remove(string filePath)
    {
        var entries = Load();
        entries.RemoveAll(e => string.Equals(e.FilePath, filePath, System.StringComparison.OrdinalIgnoreCase));
        Save(entries);
        return entries;
    }

    private void Save(List<HistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(entries, Json));
        }
        catch
        {
            // Best-effort: if the folder is unwritable, history just won't persist this session.
        }
    }
}

/// <summary>One remembered analysis. Kept small and serialisable; all fields are stored locally only.</summary>
public sealed class HistoryEntry
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "";
    public string? PackageId { get; set; }
    public string? VersionName { get; set; }
    public string? VersionCode { get; set; }
    public long FileSizeBytes { get; set; }
    public System.DateTimeOffset AnalyzedAtUtc { get; set; }

    /// <summary>True when the file is still where it was analysed — a moved/deleted file can't be re-opened.</summary>
    public bool Exists => System.IO.File.Exists(FilePath);
}
