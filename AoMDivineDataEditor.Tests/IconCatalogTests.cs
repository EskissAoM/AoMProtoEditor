using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconCatalogTests
{
    [Fact]
    public void FilterPaths_KeepsOnlyDdsBelowAllowedResourceFolders()
    {
        var result = IconCatalog.FilterPaths(
        [
            "resources/greek/player_color/units/hoplite_icon.dds",
            "resources\\aztec\\unit_icon.DDS",
            "resources/greek/readme.txt",
            "other/greek/icon.dds",
            "resources/icon.dds"
        ]);

        Assert.Equal(
        [
            "resources\\aztec\\unit_icon.png",
            "resources\\greek\\player_color\\units\\hoplite_icon.png"
        ], result);
    }

    [Theory]
    [InlineData("buttons")]
    [InlineData("clouds")]
    [InlineData("credits")]
    [InlineData("front_end_demo")]
    [InlineData("campaign")]
    [InlineData("maps")]
    [InlineData("latitude")]
    [InlineData("in_game")]
    [InlineData("postgame")]
    [InlineData("purple_smoke")]
    [InlineData("shader")]
    [InlineData("textsprites")]
    [InlineData("front_end")]
    [InlineData("glyphs")]
    [InlineData("spectator")]
    [InlineData("talking_heads")]
    public void FilterPaths_ExcludesConfiguredTopLevelResourceFolder(string folder)
    {
        Assert.Empty(IconCatalog.FilterPaths([$"resources/{folder}/sample.dds"]));
        Assert.Empty(IconCatalog.FilterPaths([$"RESOURCES/{folder.ToUpperInvariant()}/sample.DDS"]));
    }

    [Fact]
    public void FilterPaths_NormalizesAndDeduplicatesCaseInsensitively()
    {
        var result = IconCatalog.FilterPaths(
        [
            "resources/shared/sample.dds",
            "resources\\shared\\sample.dds",
            "RESOURCES/SHARED/SAMPLE.DDS"
        ]);

        Assert.Single(result);
        Assert.Equal("resources\\shared\\sample.png", result[0]);
    }

    [Fact]
    public void FilterPaths_ConvertsEveryDdsEntryToTheGameFacingPngPath()
    {
        var result = IconCatalog.FilterPaths(["resources/greek/player_color/units/hoplite_icon.dds"]);

        Assert.Equal("resources\\greek\\player_color\\units\\hoplite_icon.png", Assert.Single(result));
        Assert.DoesNotContain(result, path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NormalizeIconCatalogValue_RepairsKnownDdsValuesButPreservesUnknownCustomPaths()
    {
        var catalog = new[] { "resources\\greek\\units\\hoplite_icon.png" };

        Assert.Equal(
            "resources\\greek\\units\\hoplite_icon.png",
            ProtoEditorWindow.NormalizeIconCatalogValue("resources/greek/units/hoplite_icon.dds", catalog));
        Assert.Equal(
            "custom\\unlisted_icon.dds",
            ProtoEditorWindow.NormalizeIconCatalogValue("custom/unlisted_icon.dds", catalog));
    }

    [Fact]
    public void IconPreview_SelectsTheDefaultCulturePathBeforeOtherIcons()
    {
        var selected = IconPreviewService.SelectPreferredPath(
        [
            ("resources\\greek\\culture.png", false),
            ("resources\\shared\\default.png", true),
            ("resources\\norse\\culture.png", false)
        ]);

        Assert.Equal("resources\\shared\\default.png", selected);
    }

    [Fact]
    public void IconPreview_FallsBackToTheFirstPathWhenThereIsNoDefault()
    {
        var selected = IconPreviewService.SelectPreferredPath(
        [
            ("resources/greek/first.png", false),
            ("resources/norse/second.png", false)
        ]);

        Assert.Equal("resources/greek/first.png", selected);
    }
}
