using System.IO.Compression;
using Binoc.Core.Model;

namespace Binoc.Core.Analysis;

/// <summary>
/// One analysis pass over an opened binary (decision D3). Each analyzer fills a slice of the
/// <see cref="AnalysisReport"/> — a single category or a cohesive group. Passes are independent and run in
/// sequence by <see cref="AnalysisPipeline"/>; a pass that throws is caught and recorded as a
/// category-level error so the remaining passes still run and the report degrades gracefully.
/// </summary>
public interface IAnalyzer
{
    /// <summary>The category slug this pass owns (e.g. "identity", "signing"). Used to tag any error note
    /// raised when the pass fails.</summary>
    string Category { get; }

    /// <summary>True when this analyzer has anything to contribute for the context's format — lets the
    /// pipeline skip, say, the iOS provisioning pass for an APK.</summary>
    bool AppliesTo(BinaryFormat format);

    /// <summary>Run the pass, mutating <paramref name="report"/> in place.</summary>
    void Analyze(AnalysisContext context, AnalysisReport report);
}

/// <summary>
/// The shared, read-only view of the binary handed to every analyzer: the detected format, the opened ZIP
/// archive (all three formats are ZIPs), and the source path. Owned and disposed by
/// <see cref="AnalysisPipeline"/>.
/// </summary>
public sealed class AnalysisContext
{
    public required BinaryFormat Format { get; init; }
    public required ZipArchive Archive { get; init; }
    public required string FilePath { get; init; }
}
