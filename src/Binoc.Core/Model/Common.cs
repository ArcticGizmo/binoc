namespace Binoc.Core.Model;

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
