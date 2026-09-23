namespace Binoc.Core.Model;

/// <summary>
/// Native libraries (findings §"Native libs", Android side): the ELF <c>.so</c> files an APK/AAB ships, with
/// their ABIs and per-binary checksec (NX, RELRO, stack canary, stripped).
/// </summary>
public sealed class NativeLibsInfo
{
    /// <summary>Distinct ABIs present (e.g. arm64-v8a, armeabi-v7a, x86_64).</summary>
    public List<string> Abis { get; } = new();

    /// <summary>Each native binary and its security posture.</summary>
    public List<NativeBinary> Binaries { get; } = new();

    /// <summary>True when every native library is built for 16 KB memory pages (Android 15+ requirement).
    /// Meaningless when there are no libraries.</summary>
    public bool AllSupport16kPages => Binaries.Count > 0 && Binaries.All(b => b.Supports16kPages);
}

/// <summary>One native ELF binary and its checksec.</summary>
/// <param name="Path">Archive path.</param>
/// <param name="Abi">The ABI folder it sits under (from the path).</param>
/// <param name="Arch">Architecture from the ELF header.</param>
/// <param name="Is64Bit">64-bit ELF class.</param>
/// <param name="Nx">Non-executable stack.</param>
/// <param name="Relro">"none" / "partial" / "full".</param>
/// <param name="StackCanary">Stack-protector present.</param>
/// <param name="Stripped">No symbol table (debug symbols removed).</param>
/// <param name="Pie">Position-independent (ET_DYN).</param>
/// <param name="Supports16kPages">Loadable segments aligned to ≥16 KB (loads on 16 KB-page devices).</param>
/// <param name="LoadAlignmentBytes">Largest PT_LOAD alignment — the page size the library was built for.</param>
public sealed record NativeBinary(
    string Path, string Abi, string Arch, bool Is64Bit,
    bool Nx, string Relro, bool StackCanary, bool Stripped, bool Pie,
    bool Supports16kPages, long LoadAlignmentBytes);
