using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetDestinationPolicyTests
{
    [Fact]
    public void TryResolve_BuildsAnimDestinationInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "asset-root");

        var valid = AssetDestinationPolicy.TryResolve(root, "custom/units/special", "hoplite", ".xml", "", out var result, out _);

        Assert.True(valid);
        Assert.Equal("custom\\units\\special\\hoplite.xml", result!.RelativePath);
        Assert.Equal("custom\\units\\special\\hoplite.xml", result.XmlValue);
    }

    [Fact]
    public void TryResolve_AddsResourcesPrefixForIcons()
    {
        var root = Path.Combine(Path.GetTempPath(), "icon-root");

        var valid = AssetDestinationPolicy.TryResolve(root, "custom\\units", "hero.png", ".png", "resources", out var result, out _);

        Assert.True(valid);
        Assert.Equal("resources\\custom\\units\\hero.png", result!.XmlValue);
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData("custom\\..\\outside")]
    [InlineData("custom\\.\\outside")]
    public void TryResolve_RejectsTraversalSegments(string folder)
    {
        Assert.False(AssetDestinationPolicy.TryResolve(Path.GetTempPath(), folder, "asset", ".xml", "", out _, out _));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void TryResolve_RejectsUnsafeFileNames(string name)
    {
        Assert.False(AssetDestinationPolicy.TryResolve(Path.GetTempPath(), "custom", name, ".png", "resources", out _, out _));
    }
}
