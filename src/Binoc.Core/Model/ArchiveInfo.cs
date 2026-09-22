namespace Binoc.Core.Model;

/// <summary>
/// The archive/size walk (findings §"Optimisation / size", the shared "archive walk" module). Cheap,
/// universal, offline: entry counts, compressed vs uncompressed totals, a per-type size breakdown, and the
/// newest entry timestamp (provenance-labelled). ZIP-alignment lands as a later field.
/// </summary>
public sealed class ArchiveInfo
{
    /// <summary>Number of entries in the archive (files + directory markers).</summary>
    public int EntryCount { get; set; }

    /// <summary>Sum of the compressed (on-disk) size of every entry — what the archive "costs" stored.</summary>
    public long CompressedSize { get; set; }

    /// <summary>Sum of the uncompressed size of every entry — the installed/expanded footprint.</summary>
    public long UncompressedSize { get; set; }

    /// <summary>Per-type size breakdown, largest compressed bucket first (see <see cref="SizeBucket"/>).</summary>
    public List<SizeBucket> Buckets { get; } = new();

    /// <summary>The newest entry mtime found, with its provenance caveat — or null if none carried a usable
    /// time. Deliberately <em>not</em> presented as the build time (decision D4).</summary>
    public TimestampInfo? NewestEntry { get; set; }
}

/// <summary>One row of the per-type size breakdown — a group of entries (e.g. "DEX code", "Native
/// libraries") with their combined sizes and count.</summary>
/// <param name="Label">Human-readable group name.</param>
/// <param name="CompressedSize">Combined compressed size of the group's entries.</param>
/// <param name="UncompressedSize">Combined uncompressed size.</param>
/// <param name="EntryCount">How many entries fell into this group.</param>
public sealed record SizeBucket(string Label, long CompressedSize, long UncompressedSize, int EntryCount);
