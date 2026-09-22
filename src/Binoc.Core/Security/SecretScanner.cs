using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Binoc.Core.Model;

namespace Binoc.Core.Security;

/// <summary>
/// A bounded, high-confidence scan of an archive's <em>text</em> resources for embedded secrets. It matches
/// only unambiguous, structurally-distinctive credential shapes (private-key PEM blocks, cloud key ids,
/// provider tokens) to keep false positives near zero, and it records a finding by <b>kind and file only —
/// never the secret's value</b> (data-handling policy). Heuristic by nature (findings decision D2/D4).
/// </summary>
public static class SecretScanner
{
    private const int MaxFileBytes = 512 * 1024;
    private const int MaxFilesScanned = 3000;
    private const int MaxFindings = 200;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".xml", ".plist", ".js", ".jsx", ".ts", ".properties", ".txt", ".yml", ".yaml",
        ".env", ".cfg", ".config", ".ini", ".strings", ".html", ".htm", ".md", ".gradle", ".pem", ".key",
    };

    private static readonly (Regex Rx, string Kind)[] Patterns =
    {
        (new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |PGP |DSA )?PRIVATE KEY-----", RegexOptions.Compiled), "Private key (PEM)"),
        (new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "AWS access key id"),
        (new Regex(@"\bAIza[0-9A-Za-z_\-]{35}\b", RegexOptions.Compiled), "Google API key"),
        (new Regex(@"\bghp_[0-9A-Za-z]{36}\b", RegexOptions.Compiled), "GitHub token"),
        (new Regex(@"\bxox[baprs]-[0-9A-Za-z\-]{10,}\b", RegexOptions.Compiled), "Slack token"),
        (new Regex(@"\bsk_live_[0-9A-Za-z]{24,}\b", RegexOptions.Compiled), "Stripe live secret key"),
    };

    public static void Scan(ZipArchive archive, List<SecretFinding> sink)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int scanned = 0;

        foreach (var entry in archive.Entries)
        {
            if (sink.Count >= MaxFindings || scanned >= MaxFilesScanned) break;

            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (entry.Length == 0 || entry.Length > MaxFileBytes) continue;
            if (!TextExtensions.Contains(Path.GetExtension(name))) continue;

            scanned++;
            string text;
            try { text = ReadText(entry); }
            catch { continue; }

            foreach (var (rx, kind) in Patterns)
            {
                if (!rx.IsMatch(text)) continue;
                var key = name + "\u0000" + kind;
                if (seen.Add(key)) sink.Add(new SecretFinding(name, kind));
            }
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, MaxFileBytes));
        s.CopyTo(ms);
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
