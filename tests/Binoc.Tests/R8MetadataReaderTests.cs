using System.Text;
using Binoc.Core.Android;
using Xunit;

namespace Binoc.Tests;

public class R8MetadataReaderTests
{
    [Fact]
    public void Parses_known_fields_and_stats_percentages()
    {
        // The config flags live under "options" (R8OptionsMetadata) — the same nesting Play reads.
        var json = """
        {
          "version": "9.0.32",
          "options": {
            "isProGuardCompatibilityModeEnabled": false,
            "isOptimizationsEnabled": true,
            "isRepackageClassesEnabled": false
          },
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
        Assert.True(md.Value.FullMode);                       // = !isProGuardCompatibilityModeEnabled
        Assert.True(md.Value.OptimizationsEnabled);
        Assert.False(md.Value.RepackageClassesEnabled);
        Assert.True(md.Value.OptimizedResourceShrinkingEnabled);
        Assert.True(md.Value.ResourceShrinkingEnabled);       // implied by optimised resource shrinking
        // Positive = 100 - noXxx, rounded to nearest whole.
        Assert.Equal(88, md.Value.ObfuscationPercent);
        Assert.Equal(60, md.Value.OptimizationPercent);
        Assert.Equal(69, md.Value.ShrinkingPercent);
    }

    [Fact]
    public void Full_mode_is_the_inverse_of_proguard_compat_mode()
    {
        var compat = R8MetadataReader.Parse(Encoding.UTF8.GetBytes(
            """{ "options": { "isProGuardCompatibilityModeEnabled": true } }"""));
        Assert.False(compat!.Value.FullMode);
    }

    [Fact]
    public void Resource_shrinking_is_unknown_when_optimised_shrinking_is_off()
    {
        // r8.json carries no standalone-shrinker flag, so without optimised shrinking we can't confirm it.
        var md = R8MetadataReader.Parse(Encoding.UTF8.GetBytes(
            """{ "resourceOptimization": { "isOptimizedShrinkingEnabled": false } }"""));
        Assert.False(md!.Value.OptimizedResourceShrinkingEnabled);
        Assert.Null(md.Value.ResourceShrinkingEnabled);
    }

    [Fact]
    public void Missing_fields_are_null()
    {
        var md = R8MetadataReader.Parse(Encoding.UTF8.GetBytes("""{ "version": "8.10.0" }"""));
        Assert.NotNull(md);
        Assert.Equal("8.10.0", md!.Value.Version);
        Assert.Null(md.Value.FullMode);
        Assert.Null(md.Value.OptimizationsEnabled);
        Assert.Null(md.Value.RepackageClassesEnabled);
        Assert.Null(md.Value.ResourceShrinkingEnabled);
        Assert.Null(md.Value.OptimizedResourceShrinkingEnabled);
    }

    [Fact]
    public void Flags_at_root_instead_of_options_are_ignored()
    {
        // Guards the fix: R8 nests these under "options". A root-level flag is not the schema and must not be read.
        var json = """
        { "version": "9.0.32", "isOptimizationsEnabled": true, "isRepackageClassesEnabled": true }
        """;
        var md = R8MetadataReader.Parse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(md);
        Assert.Null(md!.Value.OptimizationsEnabled);
        Assert.Null(md.Value.RepackageClassesEnabled);
    }

    [Fact]
    public void Invalid_json_returns_null()
        => Assert.Null(R8MetadataReader.Parse(Encoding.UTF8.GetBytes("not json")));
}
