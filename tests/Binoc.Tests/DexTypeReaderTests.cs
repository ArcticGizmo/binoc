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

    [Fact]
    public void Rejects_non_dex()
    {
        Assert.Null(DexTypeReader.Read(new byte[112])); // right length, wrong magic
        Assert.Null(DexTypeReader.Read(new byte[8]));   // too short
    }
}
