using System.IO.Compression;
using System.Text.RegularExpressions;
using Binoc.Core.Android;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// Code (M3, Android): the DEX picture for APKs and AABs. Reads each <c>classes*.dex</c> header, sums method
/// references and classes, and flags multidex and proximity to the per-DEX 64K method ceiling.
/// </summary>
public sealed class CodeAnalyzer : IAnalyzer
{
    public string Category => "code";

    public bool AppliesTo(BinaryFormat format) => format is BinaryFormat.Apk or BinaryFormat.Aab;

    // APK: classes.dex, classes2.dex … at the root. AAB: <module>/dex/classes*.dex.
    private static readonly Regex DexEntry =
        new(@"(^|/)classes\d*\.dex$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Analyze(AnalysisContext context, AnalysisReport report)
    {
        var dexEntries = context.Archive.Entries
            .Where(e => DexEntry.IsMatch(e.FullName.Replace('\\', '/')))
            .ToList();
        if (dexEntries.Count == 0) return;

        var code = new CodeInfo();
        foreach (var entry in dexEntries)
        {
            var header = ReadHeader(entry, DexReader.HeaderSize);
            var name = entry.FullName.Replace('\\', '/');
            if (DexReader.Read(name, header) is not { } dex) continue;

            code.Dex.Add(dex);
            code.DexFileCount++;
            code.TotalMethodRefs += dex.MethodRefs;
            code.TotalDefinedClasses += dex.DefinedClasses;
            code.MaxMethodRefsInADex = Math.Max(code.MaxMethodRefsInADex, dex.MethodRefs);
        }

        if (code.DexFileCount == 0) return;

        code.Dex.Sort((a, b) => b.MethodRefs.CompareTo(a.MethodRefs));
        report.Code = code;

        if (code.NearMethodLimit)
            report.Notes.Add(new ReportNote("code", NoteSeverity.Info,
                $"A DEX holds {code.MaxMethodRefsInADex:N0} method references — close to the 65,536 per-DEX limit."));
    }

    // Reads up to `count` bytes from the (possibly deflate-compressed) entry without decompressing the rest.
    private static byte[] ReadHeader(ZipArchiveEntry entry, int count)
    {
        using var s = entry.Open();
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, read, count - read);
            if (n == 0) break;
            read += n;
        }
        return read == count ? buf : buf[..read];
    }
}
