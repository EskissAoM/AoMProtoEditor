using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using BCnEncoder.Shared;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconSourceIdentityTests
{
    [Theory]
    [InlineData("png", 1, "Png")]
    [InlineData("dds", 1, "Dds")]
    [InlineData("pair", 2, "Png")]
    public void CustomCatalogRetainsPhysicalFilesBehindOnePngLogicalPath(string mode, int count, string preferred)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("resources/greek");
        if (mode != "dds") File.WriteAllText(Path.Combine(root, "hero.png"), "PNG source");
        if (mode != "png") File.WriteAllText(Path.Combine(root, "hero.dds"), "DDS source");
        var before = Directory.GetFiles(root).ToDictionary(path => path, File.ReadAllBytes);
        var source = Assert.Single(CustomAssetCatalog.LoadIconSources(Path.GetDirectoryName(root)));
        Assert.Equal("resources\\greek\\hero.png", source.LogicalPath);
        Assert.Equal(count, source.PhysicalSources.Count);
        Assert.True(source.IsCustom);
        Assert.Equal(mode == "pair", source.HasCompetingCustomFiles);
        Assert.Equal(preferred, source.PreferredSource!.Format.ToString());
        Assert.All(source.PhysicalSources, file => Assert.True(Path.IsPathFullyQualified(file.Path)));
        Assert.Equal(source.LogicalPath, Assert.Single(CustomAssetCatalog.LoadIconPaths(Path.GetDirectoryName(root))));
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Theory]
    [InlineData("resources/greek/hero.png", false)]
    [InlineData("greek\\hero", false)]
    [InlineData("RESOURCES\\greek\\hero.PNG", false)]
    [InlineData("greek/hero.dds", true)]
    public async Task PreviewPreservesExactRequestAndFallbackPrecedence(string request, bool dds)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("resources");
        var directory = workspace.CreateDirectory("resources/greek");
        var pngPath = Path.Combine(directory, "hero.png");
        var ddsPath = Path.Combine(directory, "hero.dds");
        var png = AssetCreateServiceTests.Png();
        File.WriteAllBytes(pngPath, png);
        File.WriteAllBytes(ddsPath, DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba));
        var before = File.ReadAllBytes(ddsPath);
        var identity = IconSourceResolver.ResolveCustom(root, request)!;
        Assert.Equal("resources\\greek\\hero.png", identity.LogicalPath, ignoreCase: true);
        Assert.True(identity.HasCompetingCustomFiles);
        Assert.Equal(dds ? IconFileFormat.Dds : IconFileFormat.Png, identity.PreferredSource!.Format);
        var preview = await new IconPreviewService(() => null, () => root).LoadAsync(request);
        Assert.NotNull(preview);
        Assert.Equal(dds ? ddsPath : pngPath, preview.ResolvedSource, ignoreCase: true);
        if (!dds) Assert.Equal(png, preview.PngBytes);
        else Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, preview.PngBytes.Take(8));
        Assert.Equal(before, File.ReadAllBytes(ddsPath));
    }

    [Fact]
    public void ResolutionObservesCounterpartsWithoutACacheAndRefusesTraversal()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("resources");
        var png = Path.Combine(root, "hero.png");
        var dds = Path.Combine(root, "hero.dds");
        File.WriteAllText(dds, "dds");
        var first = IconSourceResolver.ResolveCustom(root, "hero.png")!;
        Assert.False(first.HasCompetingCustomFiles);
        Assert.Equal(dds, first.PreferredSource!.Path);
        File.WriteAllText(png, "png");
        var second = IconSourceResolver.ResolveCustom(root, "hero.png")!;
        Assert.True(second.HasCompetingCustomFiles);
        Assert.Equal(png, second.PreferredSource!.Path);
        File.Delete(png);
        Assert.Equal(dds, IconSourceResolver.ResolveCustom(root, "hero.png")!.PreferredSource!.Path);
        workspace.WriteText("outside.png", "outside");
        Assert.Null(IconSourceResolver.ResolveCustom(root, "..\\outside.png"));
        Assert.Null(IconSourceResolver.ResolveCustom(root, workspace.GetPath("outside.png")));
        Assert.Null(IconSourceResolver.ResolveCustom(root, "missing.png"));
        Assert.Null(IconSourceResolver.ResolveCustom(root, ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchivedDdsAndCustomOverridesRetainSeparateIdentityInManager(bool pair)
    {
        using var workspace = new TestWorkspace();
        var dds = DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba);
        using var encoded = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry("resources\\greek\\hero.dds", dds, dds.Length, false));
        var archive = workspace.GetPath("UITextureCache.bar");
        var archiveBytes = encoded.ToArray();
        File.WriteAllBytes(archive, archiveBytes);
        var originals = await IconCatalog.LoadSourcesAsync(archive);
        var original = Assert.Single(originals);
        Assert.False(original.IsCustom);
        Assert.False(original.HasCompetingCustomFiles);
        Assert.Equal("resources\\greek\\hero.png", original.LogicalPath);
        Assert.Equal("resources\\greek\\hero.dds", original.PreferredSource!.Path);
        Assert.Equal(archive, original.PreferredSource.ArchivePath);
        Assert.Equal(IconFileFormat.Dds, original.PreferredSource.Format);
        var resources = workspace.CreateDirectory("resources");
        var preview = new IconPreviewService(() => archive, () => resources);
        var archivedPreview = await preview.LoadAsync("greek/hero.png");
        Assert.NotNull(archivedPreview);
        Assert.Contains(archive, archivedPreview.ResolvedSource);
        var folder = workspace.CreateDirectory("resources/greek");
        var pngPath = Path.Combine(folder, "hero.png");
        var png = AssetCreateServiceTests.Png();
        File.WriteAllBytes(pngPath, png);
        if (pair) File.WriteAllBytes(Path.Combine(folder, "hero.dds"), dds);
        var customPreview = await preview.LoadAsync(original.LogicalPath);
        Assert.Equal(pngPath, customPreview!.ResolvedSource);
        Assert.Equal(png, customPreview.PngBytes);
        AvaloniaTestHost.Run(() =>
        {
            var manager = new IconManagerWindow(originals.Select(source => source.LogicalPath),
                CustomAssetCatalog.LoadIconPaths(resources), resources, originalSources: originals);
            try
            {
                var items = (IList)typeof(IconManagerWindow).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
                var item = Assert.IsType<IconManagerItem>(Assert.Single(items.Cast<object>()));
                Assert.True(item.IsCustom);
                Assert.Equal(original.LogicalPath, item.FullPath);
                Assert.Equal(pair ? 3 : 2, item.Source!.PhysicalSources.Count);
                Assert.Equal(pair, item.Source.HasCompetingCustomFiles);
                Assert.Equal(pngPath, item.Source.PreferredSource!.Path);
                Assert.Contains(item.Source.PhysicalSources, file => !file.IsCustom && file.ArchivePath == archive);
            }
            finally { manager.Close(); }
        });
        Assert.Equal(archiveBytes, File.ReadAllBytes(archive));
        Assert.Equal(png, File.ReadAllBytes(pngPath));
    }
}
