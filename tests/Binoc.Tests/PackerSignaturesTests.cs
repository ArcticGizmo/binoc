using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class PackerSignaturesTests
{
    [Fact]
    public void Matches_native_loader_library()
    {
        var m = PackerSignatures.Find(new[] { "lib/arm64-v8a/libjiagu.so", "classes.dex" });
        var hit = Assert.Single(m);
        Assert.Equal("360 Jiagu", hit.Product);
    }

    [Fact]
    public void Matches_marker_file()
    {
        var m = PackerSignatures.Find(new[] { "assets/ijiami.dat", "classes.dex" });
        Assert.Contains(m, x => x.Product == "Ijiami");
    }

    [Fact]
    public void Deduplicates_by_product()
    {
        var m = PackerSignatures.Find(new[]
        {
            "lib/arm64-v8a/libshella-2.10.7.0.so", "lib/armeabi-v7a/libshellx.so",
        });
        var hit = Assert.Single(m);
        Assert.Equal("Tencent Legu", hit.Product);
    }

    [Fact]
    public void Ignores_ordinary_libraries()
    {
        var m = PackerSignatures.Find(new[] { "lib/arm64-v8a/libflutter.so", "lib/arm64-v8a/libc++_shared.so" });
        Assert.Empty(m);
    }
}
