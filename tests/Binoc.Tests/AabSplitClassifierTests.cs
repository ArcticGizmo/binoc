using Binoc.Core.Android;
using Xunit;
using D = Binoc.Core.Android.AabSplitClassifier.SplitDim;

namespace Binoc.Tests;

public class AabSplitClassifierTests
{
    [Theory]
    [InlineData("base/lib/arm64-v8a/libfoo.so", D.Abi, "arm64-v8a")]
    [InlineData("base/lib/armeabi-v7a/libfoo.so", D.Abi, "armeabi-v7a")]
    [InlineData("base/res/drawable-xxhdpi-v4/ic.png", D.Density, "xxhdpi")]
    [InlineData("base/res/mipmap-hdpi/icon.png", D.Density, "hdpi")]
    [InlineData("base/res/values-fr/strings.xml", D.Language, "fr")]
    [InlineData("base/res/values-pt-rBR/strings.xml", D.Language, "pt-rBR")]
    [InlineData("base/res/layout/main.xml", D.Master, "")]
    [InlineData("base/res/drawable-nodpi/bg.png", D.Master, "")]      // density-agnostic → master
    [InlineData("base/dex/classes.dex", D.Master, "")]
    [InlineData("base/manifest/AndroidManifest.xml", D.Master, "")]
    [InlineData("base/assets/flutter_assets/x.bin", D.Master, "")]
    [InlineData("base/resources.pb", D.Master, "")]
    public void Classifies_entries(string path, D dim, string key)
    {
        var split = AabSplitClassifier.Classify(path);
        Assert.Equal(dim, split.Dim);
        Assert.Equal(key, split.Key);
    }
}
