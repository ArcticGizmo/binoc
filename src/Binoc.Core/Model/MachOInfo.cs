namespace Binoc.Core.Model;

/// <summary>
/// Mach-O (findings §"Native libs"/Code, iOS side): the app's executable — its architecture slices and,
/// per slice, PIE, FairPlay encryption (App Store binaries ship encrypted), code-signature presence and a
/// stack canary, plus the linked dylibs/frameworks.
/// </summary>
public sealed class MachOInfo
{
    /// <summary>The executable file name (CFBundleExecutable).</summary>
    public string? Executable { get; set; }

    /// <summary>True for a universal (fat) binary carrying more than one architecture.</summary>
    public bool IsFat { get; set; }

    /// <summary>Per-architecture facts.</summary>
    public List<MachOArch> Architectures { get; } = new();

    /// <summary>Linked dylibs/frameworks (union across slices, deduped).</summary>
    public List<string> LinkedLibraries { get; } = new();
}

/// <summary>One architecture slice of a Mach-O.</summary>
public sealed record MachOArch(
    string Arch, bool Is64Bit, bool Pie, bool Encrypted, bool HasCodeSignature, bool StackCanary);
