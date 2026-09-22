using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Native libraries (M3, Android): finds <c>lib/&lt;abi&gt;/*.so</c> in an APK/AAB and runs
/// <see cref="ElfReader"/> checksec over each — ABIs, NX, RELRO, stack canary, stripped. Flags a couple of
/// posture issues (unstripped ships symbols; no-canary loses stack protection).
/// </summary>
public sealed class NativeLibsAnalyzer : IAnalyzer
{
    // Cap per-file read so a pathological giant .so can't blow memory; ELF metadata is all we need.
    private const long MaxSoBytes = 96L * 1024 * 1024;

    public string Category => "native-libs";

    public bool AppliesTo(BinaryFormat format) => format is BinaryFormat.Apk or BinaryFormat.Aab;

    private static readonly Regex SoEntry =
        new(@"(^|/)lib/(?<abi>[^/]+)/(?<name>[^/]+\.so)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var info = new NativeLibsInfo();
        var abis = new HashSet<string>(StringComparer.Ordinal);
        bool anyUnstripped = false, anyNoCanary = false;

        foreach (var entry in context.Archive.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            var m = SoEntry.Match(full);
            if (!m.Success) continue;
            if (entry.Length > MaxSoBytes) continue;

            string abi = m.Groups["abi"].Value;
            var bytes = ReadFully(entry);
            if (ElfReader.Read(bytes) is not { } elf) continue;

            abis.Add(abi);
            info.Binaries.Add(new NativeBinary(full, abi, elf.Arch, elf.Is64Bit,
                elf.Nx, elf.Relro, elf.StackCanary, elf.Stripped, elf.IsPie));

            if (!elf.Stripped) anyUnstripped = true;
            if (!elf.StackCanary) anyNoCanary = true;
        }

        if (info.Binaries.Count == 0) return;

        info.Abis.AddRange(abis.OrderBy(a => a, StringComparer.Ordinal));
        info.Binaries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        report.NativeLibs = info;

        if (anyUnstripped)
            report.Notes.Add(new ReportNote("native-libs", NoteSeverity.Info,
                "Some native libraries still carry a symbol table (not stripped) — larger, and easier to reverse."));
        if (anyNoCanary)
            report.Notes.Add(new ReportNote("native-libs", NoteSeverity.Info,
                "Some native libraries have no detectable stack canary."));
    }

    private static byte[] ReadFully(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, 8 << 20));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
