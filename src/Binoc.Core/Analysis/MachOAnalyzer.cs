using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Ios;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Mach-O (M3, iOS): locates the app's main executable (via <c>CFBundleExecutable</c>) and runs
/// <see cref="MachOReader"/> over it — architectures, PIE, FairPlay encryption, code signature, canary, and
/// linked libraries.
/// </summary>
public sealed class MachOAnalyzer : IAnalyzer
{
    private const long MaxExeBytes = 200L * 1024 * 1024;

    public string Category => "macho";

    public bool AppliesTo(BinaryFormat format) => format == BinaryFormat.Ipa;

    private static readonly Regex AppInfoPlist =
        new(@"^Payload/([^/]+\.app)/Info\.plist$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        // Find the app bundle dir + its Info.plist to learn the executable name.
        var infoEntry = context.Archive.Entries
            .FirstOrDefault(e => AppInfoPlist.IsMatch(e.FullName.Replace('\\', '/')));
        if (infoEntry is null) return;

        var appDir = AppInfoPlist.Match(infoEntry.FullName.Replace('\\', '/')).Groups[1].Value;
        var plist = PlistReader.ParseDict(ReadFully(infoEntry));
        var exeName = plist.TryGetValue("CFBundleExecutable", out var v) ? Convert.ToString(v) : null;
        if (string.IsNullOrEmpty(exeName)) return;

        var exePath = $"Payload/{appDir}/{exeName}";
        var exeEntry = context.Archive.Entries
            .FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), exePath, StringComparison.OrdinalIgnoreCase));
        if (exeEntry is null || exeEntry.Length > MaxExeBytes) return;

        if (MachOReader.Read(ReadFully(exeEntry)) is not { } result) return;

        var info = new MachOInfo { Executable = exeName, IsFat = result.IsFat };
        var libs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in result.Architectures)
        {
            info.Architectures.Add(new MachOArch(a.Arch, a.Is64Bit, a.Pie, a.Encrypted, a.HasCodeSignature, a.StackCanary));
            foreach (var d in a.Dylibs) libs.Add(d);
        }
        info.LinkedLibraries.AddRange(libs.OrderBy(s => s, StringComparer.Ordinal));
        report.MachO = info;

        if (info.Architectures.Any(a => a.Encrypted))
            report.Notes.Add(new ReportNote("macho", NoteSeverity.Info,
                "The executable is FairPlay-encrypted (cryptid set) — a store-distributed binary; static "
                + "analysis of its code needs a decrypted dump."));
        if (info.Architectures.Any(a => !a.Pie))
            report.Notes.Add(new ReportNote("macho", NoteSeverity.Warning,
                "An architecture slice is not PIE (position-independent) — weaker ASLR."));
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 8 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
