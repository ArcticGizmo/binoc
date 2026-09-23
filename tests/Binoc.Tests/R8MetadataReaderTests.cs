using System.Text;
using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class R8MetadataReaderTests
{
    [Fact]
    public void Parses_known_fields_and_stats_percentages()
    {
        var json = """
        {
          "version": "9.0.32",
          "isOptimizationsEnabled": true,
          "isRepackageClassesEnabled": false,
          "resourceOptimization": { "isOptimizedShrinkingEnabled": true },
          "stats": {
            "noObfuscationPercentage": 12.4,
            "noOptimizationPercentage": 40,
            "noShrinkingPercentage": 30.6
          }
        }
        """;
        var md = R8MetadataReader.Parse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(md);
        Assert.Equal("9.0.32", md!.Value.Version);
        Assert.True(md.Value.OptimizationsEnabled);
        Assert.False(md.Value.RepackageClassesEnabled);
        Assert.True(md.Value.OptimizedResourceShrinkingEnabled);
        // Positive = 100 - noXxx, rounded to nearest whole.
        Assert.Equal(88, md.Value.ObfuscationPercent);
        Assert.Equal(60, md.Value.OptimizationPercent);
        Assert.Equal(69, md.Value.ShrinkingPercent);
    }

    [Fact]
    public void Missing_fields_are_null()
    {
        var md = R8MetadataReader.Parse(Encoding.UTF8.GetBytes("""{ "version": "8.10.0" }"""));
        Assert.NotNull(md);
        Assert.Equal("8.10.0", md!.Value.Version);
        Assert.Null(md.Value.OptimizationsEnabled);
        Assert.Null(md.Value.RepackageClassesEnabled);
        Assert.Null(md.Value.OptimizedResourceShrinkingEnabled);
    }

    [Fact]
    public void Invalid_json_returns_null()
        => Assert.Null(R8MetadataReader.Parse(Encoding.UTF8.GetBytes("not json")));
}
