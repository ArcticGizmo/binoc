using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// The archive/size walk (M1): the cheapest universal answers, shared across all three formats. Sums
/// compressed and uncompressed sizes, groups entries into a per-type breakdown, and records the newest
/// entry mtime with a provenance caveat (never presented as the build time — decision D4).
/// </summary>
public sealed class ArchiveWalkAnalyzer : IAnalyzer
{
    public string Category => "archive";

    public bool AppliesTo(BinaryFormat format) => format != BinaryFormat.Unknown;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var archive = new ArchiveInfo();
        var byBucket = new Dictionary<string, (long comp, long uncomp, int count)>();
        DateTimeOffset? newest = null;

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

            // ZIP mtimes: track the newest that isn't the DOS-epoch sentinel (1980-01-01), which is what a
            // reproducible build zeroes them to. A real time here is still only weakly trustworthy.
            var mtime = entry.LastWriteTime;
            if (mtime.Year > 1980 && (newest is null || mtime > newest))
                newest = mtime;
        }

        archive.Buckets.AddRange(byBucket
            .Select(kv => new SizeBucket(kv.Key, kv.Value.comp, kv.Value.uncomp, kv.Value.count))
            .OrderByDescending(b => b.CompressedSize));

        if (newest is { } n)
            archive.NewestEntry = new TimestampInfo(n, "Newest ZIP entry mtime",
                "ZIP timestamps are frequently zeroed or fixed for reproducible builds — treat as a weak upper "
                + "bound, not the build time.");

        report.Archive = archive;
    }
}
