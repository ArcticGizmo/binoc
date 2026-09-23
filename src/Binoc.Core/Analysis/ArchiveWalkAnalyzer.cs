using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// The archive/size walk (M1): the cheapest universal answers, shared across all three formats. Sums
/// compressed and uncompressed sizes and groups entries into a per-type breakdown. (ZIP entry mtimes are not
/// reported — reproducible builds routinely zero them, so they carry no reliable build-time signal.)
/// </summary>
public sealed class ArchiveWalkAnalyzer : IAnalyzer
{
    public string Category => "archive";

    public bool AppliesTo(BinaryFormat format) => format != BinaryFormat.Unknown;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var archive = new ArchiveInfo();
        var byBucket = new Dictionary<string, (long comp, long uncomp, int count)>();

        foreach (var entry in context.Archive.Entries)
        {
            // Directory markers (trailing '/') have zero length and no useful size — count them but don't
            // let them skew the buckets.
            var name = entry.FullName.Replace('\\', '/');
            bool isDir = name.EndsWith('/');

            archive.EntryCount++;
            archive.CompressedSize += entry.CompressedLength;
            archive.UncompressedSize += entry.Length;

            if (!isDir)
            {
                var bucket = EntryClassifier.Classify(context.Format, name);
                var cur = byBucket.TryGetValue(bucket, out var v) ? v : default;
                byBucket[bucket] = (cur.comp + entry.CompressedLength, cur.uncomp + entry.Length, cur.count + 1);
            }
        }

        archive.Buckets.AddRange(byBucket
            .Select(kv => new SizeBucket(kv.Key, kv.Value.comp, kv.Value.uncomp, kv.Value.count))
            .OrderByDescending(b => b.CompressedSize));

        // zipalign is an APK concept (the loader mmaps uncompressed entries from the installed APK).
        if (context.Format == BinaryFormat.Apk && ZipAlignment.Probe(context.FilePath) is { } alignment)
        {
            archive.Alignment = alignment;
            if (!alignment.Aligned4)
                report.Notes.Add(new ReportNote("archive", NoteSeverity.Warning,
                    $"{alignment.MisalignedCount} uncompressed entr{(alignment.MisalignedCount == 1 ? "y is" : "ies are")} "
                    + "not 4-byte aligned — the APK isn't zipaligned."));
            if (alignment.NativeLibs16k == false)
                report.Notes.Add(new ReportNote("archive", NoteSeverity.Warning,
                    "Uncompressed native libraries are not 16 KB-aligned — required for 16 KB-page devices (Android 15+)."));
        }

        report.Archive = archive;
    }
}
