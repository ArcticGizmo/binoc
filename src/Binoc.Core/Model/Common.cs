namespace Binoc.Core.Model;

/// <summary>
/// A timestamp with its provenance made explicit (decision D4). binoc never asserts a single authoritative
/// "time of creation" — ZIP mtimes are routinely zeroed for reproducible builds — so every date it shows
/// carries where it came from and how much to trust it.
/// </summary>
/// <param name="Value">The timestamp.</param>
/// <param name="Source">Short label for where it was read (e.g. "ZIP entry mtime", "cert not-before").</param>
/// <param name="ProvenanceNote">A caveat on how reliable this source is as a build time.</param>
public sealed record TimestampInfo(DateTimeOffset Value, string Source, string ProvenanceNote);
