using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Ios;
using Binoc.Core.Model;
using Binoc.Core.Signing;

namespace Binoc.Core.Analysis;

/// <summary>
/// Signing &amp; certificates (M2): who signed the binary and whether that signature can be trusted.
/// Android reads the v1 (JAR) markers plus the v2/v3/v3.1 signing block; AAB reads its upload-key JAR
/// signature and flags that it isn't the distribution key. iOS lands in a later step.
/// </summary>
public sealed class SigningAnalyzer : IAnalyzer
{
    public string Category => "signing";

    public bool AppliesTo(BinaryFormat format) => format != BinaryFormat.Unknown;

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        switch (context.Format)
        {
            case BinaryFormat.Apk: AnalyzeApk(context, report); break;
            case BinaryFormat.Aab: AnalyzeAab(context, report); break;
            case BinaryFormat.Ipa: AnalyzeIpa(context, report); break;
        }
    }

    private static readonly Regex EmbeddedProvision =
        new(@"^Payload/[^/]+\.app/embedded\.mobileprovision$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodeSignature =
        new(@"^Payload/[^/]+\.app/_CodeSignature/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void AnalyzeIpa(AnalysisContext context, AnalysisReport report)
    {
        var signing = new SigningInfo();
        var names = context.Archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

        if (names.Any(n => CodeSignature.IsMatch(n)))
            signing.Schemes.Add("Apple code signing");

        var provision = context.Archive.Entries
            .FirstOrDefault(e => EmbeddedProvision.IsMatch(e.FullName.Replace('\\', '/')));
        if (provision is not null && MobileProvisionReader.Read(ReadFully(provision)) is { } mp)
        {
            signing.Schemes.Add("embedded provisioning profile");
            if (mp.SignerCertDer is not null)
                signing.Certificate = CertSummary.From(mp.SignerCertDer);
        }
        else
        {
            report.Notes.Add(new ReportNote("signing", NoteSeverity.Info,
                "No embedded provisioning profile — App Store distribution strips it, and Apple re-signs the "
                + "app, so the shipping signing identity isn't visible here (full code-signature parsing lands in M3)."));
        }

        Finish(report, signing, noSignatureMessage: "No Apple code signature or provisioning profile found.");
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 1 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private void AnalyzeApk(AnalysisContext context, AnalysisReport report)
    {
        var signing = new SigningInfo();

        // v1 (JAR) markers from META-INF.
        var jar = JarSignatureReader.Read(context.Archive);
        if (jar.HasV1) signing.Schemes.Add("v1 (JAR)");

        // v2/v3/v3.1 from the APK Signing Block.
        var block = ApkSignatureReader.Read(context.FilePath);
        if (block is not null)
        {
            if (block.HasV2) signing.Schemes.Add("v2");
            if (block.HasV3) signing.Schemes.Add("v3");
            if (block.HasV31) signing.Schemes.Add("v3.1 (rotation)");
        }

        // Prefer the modern-scheme signer certificate; fall back to the v1 block's.
        var certDer = block?.SignerCertDer ?? jar.SignerCertDer;
        signing.Certificate = certDer is not null ? CertSummary.From(certDer) : null;

        Finish(report, signing, noSignatureMessage:
            "No signing information found (no META-INF signature and no APK Signing Block).");
    }

    private void AnalyzeAab(AnalysisContext context, AnalysisReport report)
    {
        var signing = new SigningInfo { UploadKeyNotDistribution = true };

        // An AAB is jar-signed with the upload key (Play App Signing re-signs the generated APKs).
        var jar = JarSignatureReader.Read(context.Archive);
        if (jar.HasV1) signing.Schemes.Add("v1 (JAR, upload key)");
        signing.Certificate = jar.SignerCertDer is not null ? CertSummary.From(jar.SignerCertDer) : null;

        report.Notes.Add(new ReportNote("signing", NoteSeverity.Info,
            "This is the bundle's upload key, not the distribution key. Play App Signing re-signs the APKs "
            + "generated from this AAB, so what ships to devices is signed with a different key."));

        Finish(report, signing, noSignatureMessage: "The bundle carries no upload-key signature.");
    }

    // Shared: attach the result and raise trust warnings (debug key, expired cert).
    private static void Finish(AnalysisReport report, SigningInfo signing, string noSignatureMessage)
    {
        report.Signing = signing;

        if (signing.Schemes.Count == 0 && signing.Certificate is null)
        {
            report.Notes.Add(new ReportNote("signing", NoteSeverity.Warning, noSignatureMessage));
            return;
        }

        if (signing.Certificate is { } cert)
        {
            if (cert.IsAndroidDebugKey)
                report.Notes.Add(new ReportNote("signing", NoteSeverity.Warning,
                    "Signed with the Android debug key — this build is not release-signed."));
            if (cert.IsExpiredOrNotYetValid)
                report.Notes.Add(new ReportNote("signing", NoteSeverity.Warning,
                    $"The signing certificate is outside its validity window ({cert.NotBefore:yyyy-MM-dd} – {cert.NotAfter:yyyy-MM-dd})."));
        }
    }
}
