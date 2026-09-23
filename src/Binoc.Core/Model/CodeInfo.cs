namespace Binoc.Core.Model;

/// <summary>
/// Code (findings §Code, Android side): the DEX picture. How many <c>classes*.dex</c> (multidex?), the
/// method-reference counts against the per-DEX 64K ceiling, and defined-class totals. Applies to APK and
/// AAB (both carry raw <c>.dex</c>).
/// </summary>
public sealed class CodeInfo
{
    /// <summary>Number of <c>classes*.dex</c> files.</summary>
    public int DexFileCount { get; set; }

    /// <summary>True when there's more than one DEX (the app tripped the 64K method limit at build time).</summary>
    public bool MultiDex => DexFileCount > 1;

    /// <summary>Total method references summed across every DEX.</summary>
    public long TotalMethodRefs { get; set; }

    /// <summary>Total defined classes summed across every DEX.</summary>
    public long TotalDefinedClasses { get; set; }

    /// <summary>The largest method-reference count in any single DEX (each DEX caps at 65 536).</summary>
    public int MaxMethodRefsInADex { get; set; }

    /// <summary>True when a single DEX is within ~10% of the 65 536 method-reference ceiling.</summary>
    public bool NearMethodLimit => MaxMethodRefsInADex >= 59_000;

    /// <summary>Per-DEX detail, largest method-count first.</summary>
    public List<DexFileInfo> Dex { get; } = new();
}

/// <summary>One <c>.dex</c> file's header counts.</summary>
public sealed record DexFileInfo(string Name, int MethodRefs, int DefinedClasses, int StringIds, int TypeIds, int FieldIds);
