using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CustomAssetCatalogTests
{
    [Fact]
    public void LoadIconPaths_ScansSubfoldersAndProducesGameFacingPaths()
    {
        using var directory = new TestWorkspace(nameof(CustomAssetCatalogTests));
        var nested = Path.Combine(directory.RootPath, "greek", "units");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "hoplite.png"), "test");
        File.WriteAllText(Path.Combine(nested, "legacy.dds"), "test");
        File.WriteAllText(Path.Combine(nested, "ignored.txt"), "test");

        var result = CustomAssetCatalog.LoadIconPaths(directory.RootPath);

        Assert.Equal(
        [
            "resources\\greek\\units\\hoplite.png",
            "resources\\greek\\units\\legacy.png"
        ], result);
    }

    [Fact]
    public void LoadAnimFiles_ScansSubfoldersAndKeepsOnlyXml()
    {
        using var directory = new TestWorkspace(nameof(CustomAssetCatalogTests));
        var nested = Path.Combine(directory.RootPath, "greek", "units", "hoplite");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "hoplite.xml"), "<animfile />");
        File.WriteAllText(Path.Combine(nested, "ignored.material"), "test");

        var result = CustomAssetCatalog.LoadAnimFiles(directory.RootPath);

        var item = Assert.Single(result);
        Assert.Equal("greek\\units\\hoplite\\hoplite.xml", item.Path);
        Assert.Equal("Custom", item.ArchiveName);
        Assert.True(item.IsCustom);
    }

    [Fact]
    public void LoadSoundSets_UsesTheSameRecursiveLooseXmlRules()
    {
        using var directory = new TestWorkspace(nameof(CustomAssetCatalogTests));
        var nested = Path.Combine(directory.RootPath, "custom", "units");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "voice.xml"), "<soundset />");

        var item = Assert.Single(CustomAssetCatalog.LoadSoundSets(directory.RootPath));

        Assert.Equal("custom\\units\\voice.xml", item.Path);
        Assert.True(item.IsCustom);
    }

    [Fact]
    public async Task LoadCustomXmlAsync_ReadsOnlyXmlInsideConfiguredArtRoot()
    {
        using var directory = new TestWorkspace(nameof(CustomAssetCatalogTests));
        var nested = Path.Combine(directory.RootPath, "shared");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "sample.xml"), "<animfile><anim>Idle</anim></animfile>");

        Assert.Equal(
            "<animfile><anim>Idle</anim></animfile>",
            await AnimFileCatalog.LoadCustomXmlAsync(directory.RootPath, "shared\\sample.xml"));
        Assert.Null(await AnimFileCatalog.LoadCustomXmlAsync(directory.RootPath, "..\\outside.xml"));
        Assert.Null(await AnimFileCatalog.LoadCustomXmlAsync(directory.RootPath, "shared\\sample.txt"));
    }

}
