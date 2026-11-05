using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CustomAssetDiscoverySafetyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("icons")] [InlineData("animations")] [InlineData("sounds")]
    public void MissingEmptyAndUnavailableRootsHaveDistinctResults(string kind)
    {
        using var workspace = new TestWorkspace();
        CatalogLoadState State(string? root) => kind switch
        {
            "icons" => CustomAssetCatalog.LoadIconPathsResult(root).State,
            "animations" => CustomAssetCatalog.LoadAnimFilesResult(root).State,
            _ => CustomAssetCatalog.LoadSoundSetsResult(root).State
        };
        Assert.Equal(CatalogLoadState.Missing, State(null));
        Assert.Equal(CatalogLoadState.Missing, State(workspace.GetPath("missing")));
        Assert.Equal(CatalogLoadState.Complete, State(workspace.CreateDirectory("empty")));
        var file = workspace.WriteText("occupied", "preserve");
        Assert.Equal(CatalogLoadState.Unavailable, State(file));
        Assert.Equal("preserve", File.ReadAllText(file));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AccessFailureRetainsPartialEntriesAndRetryDoesNotCacheFailure(bool afterEntry)
    {
        using var workspace = new TestWorkspace();
        var file = workspace.WriteText("assets/a.xml", "<asset/>");
        IEnumerable<string> Failed(string root)
        {
            if (afterEntry) yield return file;
            throw new UnauthorizedAccessException("fixture denied subfolder");
        }
        var failed = CustomAssetCatalog.DiscoverPaths(Path.GetDirectoryName(file), [".xml"], enumerate: Failed);
        Assert.Equal(afterEntry ? CatalogLoadState.Partial : CatalogLoadState.Unavailable, failed.State);
        Assert.False(failed.IsComplete);
        Assert.Equal(afterEntry ? 1 : 0, failed.Entries.Count);
        Assert.Contains("fixture denied subfolder", failed.Warning);
        Assert.Contains(Path.GetDirectoryName(file)!, failed.Warning);
        var retry = CustomAssetCatalog.LoadAnimFilesResult(Path.GetDirectoryName(file));
        Assert.True(retry.IsComplete);
        Assert.Single(retry.Entries);
        Assert.Empty(retry.Warning);
    }

    [Fact]
    public void MissingPathUnderAFileIsUnavailableRatherThanAnOptionalEmptyFolder()
    {
        using var workspace = new TestWorkspace();
        var parent = workspace.WriteText("ui_myth", "keep obstruction");
        var result = CustomAssetCatalog.LoadIconPathsResult(Path.Combine(parent, "resources", "nested"));
        Assert.Equal(CatalogLoadState.Unavailable, result.State);
        Assert.False(result.IsComplete);
        Assert.Contains(parent, result.Warning);
        Assert.Equal("keep obstruction", File.ReadAllText(parent));
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, true)] [InlineData(3, true)]
    public void EnumerationAndAssetLimitsRefusePartialListsAsComplete(int limit, bool complete)
    {
        using var workspace = new TestWorkspace();
        workspace.WriteText("assets/a.xml", "a");
        workspace.WriteText("assets/b.xml", "b");
        var root = workspace.GetPath("assets");
        var entries = CustomAssetCatalog.DiscoverPaths(root, [".xml"], maximumEntries: limit);
        var assets = CustomAssetCatalog.DiscoverPaths(root, [".xml"], maximumAssets: limit);
        Assert.Equal(complete, entries.IsComplete);
        Assert.Equal(complete, assets.IsComplete);
        if (!complete)
        {
            Assert.Equal(CatalogLoadState.Partial, entries.State);
            Assert.Single(entries.Entries);
            Assert.Contains("limit", entries.Warning);
            Assert.Contains("asset limit", assets.Warning);
        }
    }

    [Fact]
    public void EmptyDirectoriesAndUnsupportedFilesConsumeTraversalBudget()
    {
        using var workspace = new TestWorkspace();
        workspace.CreateDirectory("assets/empty");
        workspace.WriteText("assets/ignored.txt", "x");
        var result = CustomAssetCatalog.DiscoverPaths(workspace.GetPath("assets"), [".xml"], maximumEntries: 1);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.Contains("entry limit", result.Warning);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LinkedRootsAndSubfoldersDoNotExposeExternalAssets(bool rootLink)
    {
        using var workspace = new TestWorkspace();
        var assets = workspace.CreateDirectory("assets");
        var outside = workspace.CreateDirectory("outside");
        workspace.WriteText("outside/secret.xml", "outside unchanged");
        workspace.WriteText("assets/local.xml", "local unchanged");
        var link = workspace.GetPath(rootLink ? "linked-root" : "assets/linked-folder");
        AssetCreateServiceTests.CreateDirectoryLink(link, outside);
        try
        {
            var result = CustomAssetCatalog.LoadAnimFilesResult(rootLink ? link : assets);
            Assert.Equal(rootLink ? CatalogLoadState.Unavailable : CatalogLoadState.Partial, result.State);
            Assert.False(result.IsComplete);
            Assert.DoesNotContain(result.Entries, entry => entry.Path.Contains("secret", StringComparison.Ordinal));
            Assert.Contains("link", result.Warning, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("outside unchanged", File.ReadAllText(workspace.GetPath("outside/secret.xml")));
        }
        finally { Directory.Delete(link); } // Remove only this fixture's link, never its target.
    }

    [Fact]
    public void HiddenAndSystemAssetsRemainSuggestionsAndPairedPhysicalIdentityIsRetained()
    {
        using var workspace = new TestWorkspace();
        var png = workspace.WriteText("assets/hidden/pair.png", "png");
        var dds = workspace.WriteText("assets/hidden/pair.dds", "dds");
        var directory = Path.GetDirectoryName(png)!;
        File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.Hidden | FileAttributes.System);
        try
        {
            var result = CustomAssetCatalog.LoadIconSourcesResult(workspace.GetPath("assets"));
            Assert.True(result.IsComplete);
            var source = Assert.Single(result.Entries);
            Assert.Equal("resources\\hidden\\pair.png", source.LogicalPath);
            Assert.True(source.HasCompetingCustomFiles);
            Assert.Equal(2, source.PhysicalSources.Count);
            Assert.Contains(source.PhysicalSources, item => item.Path == png && item.Format == IconFileFormat.Png);
            Assert.Contains(source.PhysicalSources, item => item.Path == dds && item.Format == IconFileFormat.Dds);
        }
        finally { File.SetAttributes(directory, FileAttributes.Directory); }
    }

    [Fact]
    public void DisappearedRootAndEscapedAdapterCannotProduceACompleteSnapshot()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("assets");
        IEnumerable<string> Disappeared(string directory)
        {
            Directory.Delete(directory);
            yield break;
        }
        Assert.Equal(CatalogLoadState.Unavailable, CustomAssetCatalog.DiscoverPaths(root, [".xml"], enumerate: Disappeared).State);
        Directory.CreateDirectory(root);
        var outside = workspace.WriteText("outside.xml", "outside");
        var escaped = CustomAssetCatalog.DiscoverPaths(root, [".xml"], enumerate: _ => [outside]);
        Assert.False(escaped.IsComplete);
        Assert.Empty(escaped.Entries);
    }

    [Fact]
    public void HostRetainsKnownSuggestionsOnFailureWithoutDirtyingOrLeakingAcrossMods()
    {
        using var fixture = new BaselineSafetyFixture();
        var asset = fixture.Workspace.WriteText("TestMod/game/art/custom.xml", "<asset/>");
        var modBefore = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Call("LoadCurrentModAssetCatalogs");
                Assert.Single(HostField<IReadOnlyList<AnimFileCatalogEntry>>(host, "_customAnimFiles"));
                File.Delete(asset);
                Directory.Delete(Path.GetDirectoryName(asset)!);
                File.WriteAllText(Path.GetDirectoryName(asset)!, "blocked root");
                host.Call("LoadCurrentModAssetCatalogs");
                Assert.Single(HostField<IReadOnlyList<AnimFileCatalogEntry>>(host, "_customAnimFiles"));
                Assert.Contains("incomplete", HostField<TextBlock>(host, "_statusMessage").Text);
                Assert.False(HostField<bool>(host, "_isDirty"));
                Assert.Equal(modBefore, File.ReadAllBytes(fixture.ProtoPath));
                var other = fixture.Workspace.WriteXml("Other/game/data/gameplay/proto_mods.xml", "<protomods/>");
                fixture.Workspace.WriteText("Other/game/art", "also blocked");
                host.Set("_modFilePath", other);
                host.Call("LoadCurrentModAssetCatalogs");
                Assert.Empty(HostField<IReadOnlyList<AnimFileCatalogEntry>>(host, "_customAnimFiles"));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(AssetReferenceKind.Icon, false)] [InlineData(AssetReferenceKind.Icon, true)]
    [InlineData(AssetReferenceKind.Animation, false)] [InlineData(AssetReferenceKind.Animation, true)]
    [InlineData(AssetReferenceKind.SoundSet, false)] [InlineData(AssetReferenceKind.SoundSet, true)]
    public void ActualManagerOpeningCanCancelOrRetryAnUnavailableCustomRoot(AssetReferenceKind kind, bool retry)
    {
        using var fixture = new BaselineSafetyFixture();
        ArchiveCatalogAvailabilityTests.WriteArchive(fixture.Workspace, "IsolatedGame/ui/UITextureCache.bar", "resources/test/icon.dds");
        ArchiveCatalogAvailabilityTests.WriteArchive(fixture.Workspace, "IsolatedGame/art/ArtTest.bar", "test/unit.xml");
        ArchiveCatalogAvailabilityTests.WriteArchive(fixture.Workspace, "IsolatedGame/sound/Sound.bar", "test/unit.xml");
        var relative = kind == AssetReferenceKind.Icon ? "TestMod/game/ui_myth/resources" :
            kind == AssetReferenceKind.Animation ? "TestMod/game/art" : "TestMod/game/sound";
        var blocked = fixture.Workspace.WriteText(relative, "blocked root");
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            try
            {
                host.Call("InitializeEditorTabs");
                host.Set("_baseGameIconPaths", new[] { "resources/original.png" });
                host.Set("_baseGameAnimFiles", new[] { new AnimFileCatalogEntry("original.xml", "ArtGreek.bar") });
                host.Set("_baseGameSoundSets", new[] { new AnimFileCatalogEntry("original.xml", "Sound.bar") });
                var operation = (Task)host.Call(kind == AssetReferenceKind.Icon ? "OpenIconManagerAsync" :
                    kind == AssetReferenceKind.Animation ? "OpenAnimFileManagerAsync" : "OpenSoundSetManagerAsync")!;
                var prompt = await OwnedAsync<Prompt>(host);
                Assert.Contains(blocked, prompt.PromptText);
                Assert.Contains("incomplete", prompt.PromptTitle);
                if (retry)
                {
                    File.Delete(blocked);
                    Directory.CreateDirectory(blocked);
                    fixture.Workspace.WriteText(relative + (kind == AssetReferenceKind.Icon ? "/new.png" : "/new.xml"), "asset");
                    prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (kind == AssetReferenceKind.Icon) (await OwnedAsync<IconManagerWindow>(host)).Close();
                    else (await OwnedAsync<AnimFileManagerWindow>(host)).Close();
                }
                else prompt.Close();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(host.OwnedWindows);
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                if (!retry) Assert.Equal("blocked root", File.ReadAllText(blocked));
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LateDiscoveryFailureBlocksImportAndDuplicationBeforeAnyWrite(bool icon)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.WriteText("blocked", "keep root");
        var source = workspace.WriteText("source.png", "source unchanged");
        var destination = new AssetDestination(Path.Combine(root, icon ? "new.png" : "new.xml"), "new", "new");
        AvaloniaTestHost.RunAsync(async () =>
        {
            Window manager = icon ? new IconManagerWindow([], [], root) :
                new AnimFileManagerWindow([], _ => Task.FromResult<string?>("<asset/>"), root);
            manager.Show();
            try
            {
                var operation = (Task)manager.GetType().GetMethod(icon ? "ImportToDestinationAsync" : "DuplicateToDestinationAsync", Private)!
                    .Invoke(manager, [icon ? source : "<asset/>", destination])!;
                (await OwnedAsync<Prompt>(manager)).Close();
                await operation.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal("keep root", File.ReadAllText(root));
                Assert.Equal("source unchanged", File.ReadAllText(source));
                Assert.False(File.Exists(destination.AbsolutePath));
            }
            finally { foreach (var child in manager.OwnedWindows.ToArray()) child.Close(); manager.Close(); }
        });
    }

    private static T HostField<T>(BaselineHost host, string name)
        => (T)typeof(ProtoEditorWindow).GetField(name, Private)!.GetValue(host)!;

    private static async Task<T> OwnedAsync<T>(Window owner) where T : Window
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (owner.OwnedWindows.OfType<T>().SingleOrDefault() is { } window) return window;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {typeof(T).Name}.");
    }
}
