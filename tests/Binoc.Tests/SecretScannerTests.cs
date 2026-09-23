using System.IO.Compression;
using System.Text;
using Binoc.Core.Model;
using Binoc.Core.Security;
using Xunit;

namespace Binoc.Tests;

public class SecretScannerTests
{
    private static ZipArchive ZipWith(params (string name, string content)[] files)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
                using (var s = zip.CreateEntry(name).Open())
                    s.Write(Encoding.UTF8.GetBytes(content));
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    [Fact]
    public void Flags_known_secret_shapes_by_kind_and_file_only()
    {
        // Synthetic, non-real credential shapes — structurally matching, no live value.
        using var zip = ZipWith(
            ("assets/config.json", "{\"key\":\"AKIAAAAAAAAAEXAMPLE7\"}"),            // AWS shape (20 chars)
            ("assets/app.js", "const k='AIza01234567890123456789012345678901234';"), // Google API key shape (AIza + 35)
            ("res/raw/id.pem", "-----BEGIN RSA PRIVATE KEY-----\nZm9v\n-----END RSA PRIVATE KEY-----"),
            ("assets/clean.json", "{\"hello\":\"world\"}"));                          // nothing

        var findings = new List<SecretFinding>();
        SecretScanner.Scan(zip, findings);

        Assert.Contains(findings, f => f.Kind == "AWS access key id" && f.File == "assets/config.json");
        Assert.Contains(findings, f => f.Kind == "Google API key" && f.File == "assets/app.js");
        Assert.Contains(findings, f => f.Kind == "Private key (PEM)" && f.File == "res/raw/id.pem");
        Assert.DoesNotContain(findings, f => f.File == "assets/clean.json");

        // The value is never captured — only kind + file.
        Assert.All(findings, f => Assert.DoesNotContain("AKIA", f.Kind + f.File));
    }

    [Fact]
    public void Ignores_binary_and_non_text_extensions()
    {
        using var zip = ZipWith(
            ("lib/arm64-v8a/x.so", "AKIAAAAAAAAAEXAMPLE7"),   // .so is not scanned
            ("classes.dex", "-----BEGIN RSA PRIVATE KEY-----"));
        var findings = new List<SecretFinding>();
        SecretScanner.Scan(zip, findings);
        Assert.Empty(findings);
    }
}
