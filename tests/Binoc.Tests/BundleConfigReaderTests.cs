using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class BundleConfigReaderTests
{
    [Fact]
    public void Defaults_when_no_config()
    {
        var r = BundleConfigReader.Resolve(null);
        Assert.False(r.FromConfig);
        Assert.Equal(new[] { "ABI", "Screen density", "Language" }, r.EnabledDimensions);
    }

    [Fact]
    public void Honours_a_negated_dimension_from_config()
    {
        // BundleConfig{ optimizations=2 { splits_config=1 { split_dimension=1 { value=3(LANGUAGE), negate=true } } } }
        var splitDimension = Concat(VarintField(1, 3), VarintField(2, 1));      // LANGUAGE, negate
        var splitsConfig = LenField(1, splitDimension);
        var optimizations = LenField(1, splitsConfig);
        var bundleConfig = LenField(2, optimizations);

        var r = BundleConfigReader.Resolve(bundleConfig);

        Assert.True(r.FromConfig);
        Assert.Equal(new[] { "ABI", "Screen density" }, r.EnabledDimensions); // Language disabled
    }

    // ── minimal protobuf wire encoder ──
    private static byte[] Varint(ulong v)
    {
        var b = new List<byte>();
        while (v >= 0x80) { b.Add((byte)((v & 0x7F) | 0x80u)); v >>= 7; }
        b.Add((byte)v);
        return b.ToArray();
    }
    private static byte[] Tag(int number, int wire) => Varint(((ulong)(uint)number << 3) | (uint)wire);
    private static byte[] Concat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p, 0, p.Length);
        return ms.ToArray();
    }
    private static byte[] VarintField(int number, ulong value) => Concat(Tag(number, 0), Varint(value));
    private static byte[] LenField(int number, byte[] payload) => Concat(Tag(number, 2), Varint((ulong)payload.Length), payload);
}
