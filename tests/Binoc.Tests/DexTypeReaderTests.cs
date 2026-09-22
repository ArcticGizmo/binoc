using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class DexTypeReaderTests
{
    [Theory]
    [InlineData("La;", true)]              // single-char, top-level
    [InlineData("Lcom/example/a;", true)]  // single-char simple name
    [InlineData("Lcom/a/bc;", true)]       // two-char lowercase (dictionary-style)
    [InlineData("Lcom/a/b$c;", true)]      // nested, mangled inner token
    [InlineData("Lcom/example/MainActivity;", false)]
    [InlineData("Lcom/example/R;", false)] // uppercase single char isn't dictionary-style
    [InlineData("Lcom/example/Helper;", false)]
    [InlineData("Ljava/lang/String;", false)]
    public void Classifies_mangled_descriptors(string descriptor, bool expected)
        => Assert.Equal(expected, DexTypeReader.IsMangledClassDescriptor(descriptor));

    [Fact]
    public void Reads_defined_classes_and_counts_mangled()
    {
        var dex = DexTestBuilder.Build(
            "Lcom/example/a;", "Lcom/example/b;", "Lcom/example/c;",  // mangled
            "Lcom/example/MainActivity;");                            // not

        var stats = DexTypeReader.Read(dex);

        Assert.NotNull(stats);
        Assert.Equal(4, stats!.Value.DefinedClasses);
        Assert.Equal(3, stats.Value.MangledClasses);
    }

    [Theory]
    [InlineData("Lcom/example/a;", "com/example")]
    [InlineData("La;", "")]
    [InlineData("Lcom/a/b$c;", "com/a")]
    public void Extracts_package(string descriptor, string pkg)
        => Assert.Equal(pkg, DexTypeReader.PackageOf(descriptor));

    [Fact]
    public void Reports_dominant_package_for_repackaged_dex()
    {
        // Everything collapsed into the default package (root) — the repackageclasses fingerprint.
        var dex = DexTestBuilder.Build("La;", "Lb;", "Lc;", "Ld;");
        var stats = DexTypeReader.Read(dex)!.Value;
        Assert.Equal("", stats.TopPackage);
        Assert.Equal(4, stats.TopPackageCount);
    }

    [Fact]
    public void Counts_defined_methods_and_fields()
    {
        // One class with mangled + readable methods and fields; constructors must be ignored.
        var dex = DexTestBuilder.BuildClassWithMembers(
            "Lcom/example/MainActivity;",
            methodNames: new[] { "a", "b", "onCreate", "<init>" },
            fieldNames: new[] { "a", "userName" });
        var s = DexTypeReader.Read(dex)!.Value;

        Assert.Equal(1, s.DefinedClasses);
        Assert.Equal(0, s.MangledClasses);              // MainActivity isn't mangled
        Assert.Equal(3, s.DefinedMethods);              // a, b, onCreate — <init> excluded
        Assert.Equal(2, s.MangledMethods);              // a, b
        Assert.Equal(2, s.DefinedFields);               // a, userName
        Assert.Equal(1, s.MangledFields);               // a
        Assert.Equal(6, s.DefinedSymbols);              // 1 class + 3 methods + 2 fields
        Assert.Equal(3, s.MangledSymbols);              // a(method), b(method), a(field)
    }

    [Fact]
    public void Rejects_non_dex()
    {
        Assert.Null(DexTypeReader.Read(new byte[112])); // right length, wrong magic
        Assert.Null(DexTypeReader.Read(new byte[8]));   // too short
    }
}
