using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CustomAssetCatalogTests
{
    [Fact]
    public void LoadIconPaths_ScansSubfoldersAndProducesGameFacingPaths()
    {
        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, "greek", "units");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "hoplite.png"), "test");
        File.WriteAllText(Path.Combine(nested, "legacy.dds"), "test");
        File.WriteAllText(Path.Combine(nested, "ignored.txt"), "test");

        var result = CustomAssetCatalog.LoadIconPaths(directory.Path);

        Assert.Equal(
        [
            "resources\\greek\\units\\hoplite.png",
            "resources\\greek\\units\\legacy.png"
        ], result);
    }

    [Fact]
    public void LoadAnimFiles_ScansSubfoldersAndKeepsOnlyXml()
    {
        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, "greek", "units", "hoplite");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "hoplite.xml"), "<animfile />");
        File.WriteAllText(Path.Combine(nested, "ignored.material"), "test");

        var result = CustomAssetCatalog.LoadAnimFiles(directory.Path);

        var item = Assert.Single(result);
        Assert.Equal("greek\\units\\hoplite\\hoplite.xml", item.Path);
        Assert.Equal("Custom", item.ArchiveName);
        Assert.True(item.IsCustom);
    }

    [Fact]
    public void LoadSoundSets_UsesTheSameRecursiveLooseXmlRules()
    {
        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, "custom", "units");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "voice.xml"), "<soundset />");

        var item = Assert.Single(CustomAssetCatalog.LoadSoundSets(directory.Path));

        Assert.Equal("custom\\units\\voice.xml", item.Path);
        Assert.True(item.IsCustom);
    }

    [Fact]
    public async Task LoadCustomXmlAsync_ReadsOnlyXmlInsideConfiguredArtRoot()
    {
        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, "shared");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "sample.xml"), "<animfile><anim>Idle</anim></animfile>");

        Assert.Equal(
            "<animfile><anim>Idle</anim></animfile>",
            await AnimFileCatalog.LoadCustomXmlAsync(directory.Path, "shared\\sample.xml"));
        Assert.Null(await AnimFileCatalog.LoadCustomXmlAsync(directory.Path, "..\\outside.xml"));
        Assert.Null(await AnimFileCatalog.LoadCustomXmlAsync(directory.Path, "shared\\sample.txt"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AoMDivineDataEditor.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
