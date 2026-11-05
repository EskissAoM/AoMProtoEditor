using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Reflection;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ArchiveCatalogAvailabilityTests
{
    [Theory]
    [InlineData("icon")] [InlineData("animation")] [InlineData("sound")]
    public async Task RequiredMissingMalformedAndGenuinelyEmptySourcesAreDistinct(string kind)
    {
        using var workspace = new TestWorkspace();
        var missing = workspace.GetPath(kind == "animation" ? "missing/ArtTest.bar" : "missing.bar");
        var unavailable = await ReadResultAsync(kind, missing);
        Assert.Equal(CatalogLoadState.Unavailable, unavailable.State);
        Assert.False(unavailable.IsComplete);
        Assert.NotEmpty(unavailable.Diagnostics);
        var empty = WriteArchive(workspace, "empty/ArtTest.bar");
        var complete = await ReadResultAsync(kind, empty);
        Assert.Equal(CatalogLoadState.Complete, complete.State);
        Assert.Empty(complete.Entries);
        Assert.Empty(complete.Diagnostics);
        var malformed = workspace.WriteText("malformed/ArtTest.bar", "not an archive");
        var failed = await ReadResultAsync(kind, malformed);
        Assert.Equal(CatalogLoadState.Unavailable, failed.State);
        Assert.Contains("FileTooSmall", failed.Warning);
        Assert.Equal("not an archive", File.ReadAllText(malformed));
    }

    [Theory]
    [InlineData("icon")] [InlineData("animation")] [InlineData("sound")]
    public async Task ListingNeverDecodesInvalidCompressedPayloadsAndReplacesRevisions(string kind)
    {
        using var workspace = new TestWorkspace();
        var first = kind == "icon" ? "resources/test/first.dds" : "test/first.xml.XMB";
        var second = first.Replace("first", "other");
        var path = WriteArchive(workspace, "ArtTest.bar", first);
        var initial = await ReadResultAsync(kind, path);
        Assert.Equal(CatalogLoadState.Complete, initial.State);
        Assert.Contains("first", Assert.Single(initial.Entries));
        if (kind == "icon")
        {
            var sources = await IconCatalog.LoadSourcesResultAsync(path);
            var source = Assert.Single(sources.Entries);
            Assert.EndsWith(".png", source.LogicalPath);
            Assert.Equal(first, Assert.Single(source.PhysicalSources).Path.Replace('\\', '/'));
        }
        var stamp = File.GetLastWriteTimeUtc(path);
        WriteArchive(workspace, "ArtTest.bar", second);
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
        var updated = await ReadResultAsync(kind, path);
        Assert.Equal(CatalogLoadState.Complete, updated.State);
        Assert.Contains("other", Assert.Single(updated.Entries));
    }

    [Fact]
    public async Task ArtPartialResultsIdentifyFailuresRetryAndRecheckMembership()
    {
        using var workspace = new TestWorkspace();
        var good = WriteArchive(workspace, "ArtGreek.bar", "greek/unit.xml");
        var bad = WriteArchive(workspace, "ArtOther.bar", "other/unit.xml");
        var valid = File.ReadAllBytes(bad);
        var invalid = (byte[])valid.Clone(); invalid[0] = 0;
        File.WriteAllBytes(bad, invalid);
        var stamp = File.GetLastWriteTimeUtc(bad);
        workspace.WriteText("ArtUI.bar", "excluded malformed archive");
        workspace.WriteText("ArtExamplesForModders.bar", "excluded malformed archive");
        var partial = await AnimFileCatalog.LoadResultAsync(workspace.RootPath);
        Assert.Equal(CatalogLoadState.Partial, partial.State);
        Assert.Single(partial.Entries);
        Assert.Contains("1 of 2", partial.Warning);
        Assert.Contains(bad, partial.Warning);
        Assert.DoesNotContain("ArtUI", partial.Warning);
        File.WriteAllBytes(bad, valid); File.SetLastWriteTimeUtc(bad, stamp);
        var recovered = await AnimFileCatalog.LoadResultAsync(workspace.RootPath);
        Assert.True(recovered.IsComplete);
        Assert.Equal(2, recovered.Entries.Count);
        File.Delete(good);
        WriteArchive(workspace, "ArtNew.bar", "new/unit.xml");
        var membership = await AnimFileCatalog.LoadResultAsync(workspace.RootPath);
        Assert.Equal(2, membership.Entries.Count);
        Assert.DoesNotContain(membership.Entries, entry => entry.ArchiveName == "ArtGreek.bar");
        Assert.Contains(membership.Entries, entry => entry.ArchiveName == "ArtNew.bar");
    }

    [Theory]
    [InlineData("icon")] [InlineData("animation")] [InlineData("sound")]
    public async Task MalformedArchiveCanRecoverWithoutMetadataChange(string kind)
    {
        using var workspace = new TestWorkspace();
        var path = WriteArchive(workspace, "ArtTest.bar", kind == "icon" ? "resources/test/icon.dds" : "test/unit.xml");
        var valid = File.ReadAllBytes(path);
        var invalid = (byte[])valid.Clone();
        invalid[0] = 0;
        File.WriteAllBytes(path, invalid);
        var stamp = File.GetLastWriteTimeUtc(path);
        Assert.Equal(0, await CountLegacyAsync(kind, path));
        File.WriteAllBytes(path, valid);
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal(1, await CountLegacyAsync(kind, path));
    }

    internal static string WriteArchive(TestWorkspace workspace, string relative, params string[] entries)
    {
        var path = workspace.GetPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = BarArchiveTests.BuildArchive(entries.Select(entry =>
            new BarArchiveTests.TestEntry(entry, [0xff, 0, 0xff], 100, true)).ToArray());
        File.WriteAllBytes(path, archive.ToArray());
        return path;
    }

    private static async Task<int> CountLegacyAsync(string kind, string path) => kind switch
    {
        "icon" => (await IconCatalog.LoadAsync(path)).Count,
        "animation" => (await AnimFileCatalog.LoadAsync(Path.GetDirectoryName(path)!)).Count,
        _ => (await SoundSetCatalog.LoadAsync(path)).Count
    };

    private static async Task<CatalogLoadResult<string>> ReadResultAsync(string kind, string path) => kind switch
    {
        "icon" => await IconCatalog.LoadResultAsync(path),
        "animation" => (await AnimFileCatalog.LoadResultAsync(Path.GetDirectoryName(path)!)).Map<string>(items => items.Select(item => item.Path).ToList()),
        _ => (await SoundSetCatalog.LoadResultAsync(path)).Map<string>(items => items.Select(item => item.Path).ToList())
    };

    [Theory]
    [InlineData(AssetReferenceKind.Icon, false)] [InlineData(AssetReferenceKind.Icon, true)]
    [InlineData(AssetReferenceKind.Animation, false)] [InlineData(AssetReferenceKind.Animation, true)]
    [InlineData(AssetReferenceKind.SoundSet, false)] [InlineData(AssetReferenceKind.SoundSet, true)]
    public void ActualManagersExplainArchiveFailureAndAllowCancelOrRetry(AssetReferenceKind kind, bool retry)
    {
        using var fixture = new BaselineSafetyFixture();
        var relative = kind == AssetReferenceKind.Icon ? "IsolatedGame/ui/UITextureCache.bar" :
            kind == AssetReferenceKind.Animation ? "IsolatedGame/art/ArtTest.bar" : "IsolatedGame/sound/Sound.bar";
        var entry = kind == AssetReferenceKind.Icon ? "resources/test/icon.dds" : "test/unit.xml";
        var archive = WriteArchive(fixture.Workspace, relative, entry);
        var valid = File.ReadAllBytes(archive);
        var invalid = (byte[])valid.Clone(); invalid[0] = 0;
        File.WriteAllBytes(archive, invalid);
        var stamp = File.GetLastWriteTimeUtc(archive);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            try
            {
                host.Call("InitializeEditorTabs");
                var operation = (Task)host.Call(kind == AssetReferenceKind.Icon ? "OpenIconManagerAsync" :
                    kind == AssetReferenceKind.Animation ? "OpenAnimFileManagerAsync" : "OpenSoundSetManagerAsync")!;
                var prompt = await OwnedAsync<Prompt>(host);
                Assert.Contains(archive, prompt.PromptText);
                Assert.Contains("InvalidHeader", prompt.PromptText);
                Assert.Contains("incomplete", prompt.PromptTitle);
                if (retry)
                {
                    File.WriteAllBytes(archive, valid); File.SetLastWriteTimeUtc(archive, stamp);
                    prompt.FindControl<Button>("_confirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (kind == AssetReferenceKind.Icon) (await OwnedAsync<IconManagerWindow>(host)).Close();
                    else (await OwnedAsync<AnimFileManagerWindow>(host)).Close();
                }
                else prompt.Close();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(host.OwnedWindows);
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                Assert.Equal(retry ? valid : invalid, File.ReadAllBytes(archive));
                Assert.False(Field<bool>(host, "_isDirty"));
            }
            finally
            {
                foreach (var child in host.OwnedWindows.ToArray()) child.Close();
                host.DisposeFixtureWindow();
            }
        });
    }

    [Theory]
    [InlineData(AssetReferenceKind.Icon)] [InlineData(AssetReferenceKind.Animation)] [InlineData(AssetReferenceKind.SoundSet)]
    public void HostRetainsSuggestionsOnlyForTheSameSourceAndDoesNotDirtyXml(AssetReferenceKind kind)
    {
        using var fixture = new BaselineSafetyFixture();
        var file = kind == AssetReferenceKind.Icon ? "ui/UITextureCache.bar" :
            kind == AssetReferenceKind.Animation ? "art/ArtTest.bar" : "sound/Sound.bar";
        var archive = WriteArchive(fixture.Workspace, "IsolatedGame/" + file,
            kind == AssetReferenceKind.Icon ? "resources/test/icon.dds" : "test/unit.xml");
        var method = kind == AssetReferenceKind.Icon ? "LoadBaseGameIconCatalogAsync" :
            kind == AssetReferenceKind.Animation ? "LoadBaseGameAnimFileCatalogAsync" : "LoadBaseGameSoundSetCatalogAsync";
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                await (Task)host.Call(method, fixture.Workspace.GetPath("IsolatedGame/data/Data.bar"))!;
                var catalogs = Field<Dictionary<AssetReferenceKind, CatalogLoadResult<string>>>(host, "_baseGameAssetCatalogLoads");
                var expected = Assert.Single(catalogs[kind].Entries);
                File.WriteAllText(archive, "malformed now");
                await (Task)host.Call(method, fixture.Workspace.GetPath("IsolatedGame/data/Data.bar"))!;
                Assert.False(catalogs[kind].IsComplete);
                Assert.Contains("incomplete", Field<TextBlock>(host, "_statusMessage").Text);
                Assert.Contains(expected, BasePaths(host, kind));
                var otherData = fixture.Workspace.WriteText("OtherGame/data/Data.bar", "isolated");
                await (Task)host.Call(method, otherData)!;
                Assert.Empty(BasePaths(host, kind));
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                Assert.False(Field<bool>(host, "_isDirty"));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    private static IEnumerable<string> BasePaths(BaselineHost host, AssetReferenceKind kind)
        => kind == AssetReferenceKind.Icon ? Field<IReadOnlyList<string>>(host, "_baseGameIconPaths") :
            Field<IReadOnlyList<AnimFileCatalogEntry>>(host, kind == AssetReferenceKind.Animation ? "_baseGameAnimFiles" : "_baseGameSoundSets").Select(item => item.Path);

    private static T Field<T>(BaselineHost host, string name)
        => (T)typeof(ProtoEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    private static async Task<T> OwnedAsync<T>(Window owner) where T : Window
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (owner.OwnedWindows.OfType<T>().SingleOrDefault() is { } dialog) return dialog;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {typeof(T).Name}.");
    }
}
