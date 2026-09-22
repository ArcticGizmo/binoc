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

/// <summary>
/// A parsed X.509 signing certificate, reduced to the fields the report shows (findings §Signing). Shared
/// by all three formats — the certificate is the common currency of "who signed this".
/// </summary>
public sealed record CertInfo(
    string Subject,
    string Issuer,
    string Sha256Fingerprint,
    string Sha1Fingerprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool SelfSigned,
    bool IsAndroidDebugKey,
    string SignatureAlgorithm,
    int KeySizeBits)
{
    /// <summary>True when the certificate's validity window doesn't contain the current instant.</summary>
    public bool IsExpiredOrNotYetValid => DateTimeOffset.UtcNow < NotBefore || DateTimeOffset.UtcNow > NotAfter;
}
