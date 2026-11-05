using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BCnEncoder.Shared;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class IconManagerLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static List<IconManagerItem> Items(IconManagerWindow manager) => Field<List<IconManagerItem>>(manager, "_items");
    private static async Task<T> DialogAsync<T>(Window owner) where T : Window
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (owner.OwnedWindows.OfType<T>().SingleOrDefault() is { } dialog) return dialog;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {typeof(T).Name}.");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("dds")]
    public async Task MovingOverrideRestoresOriginalMergesTargetAndSurvivesReopen(string extension)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='IconUnit'><icon>resources/greek/old.png</icon><future keep='yes'/></unit></protomods>");
        var resources = fixture.Workspace.CreateDirectory("TestMod/game/ui_myth/resources");
        var source = Path.Combine(fixture.Workspace.CreateDirectory("TestMod/game/ui_myth/resources/greek"), "old." + extension);
        var bytes = extension == "dds" ? DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba) : AssetCreateServiceTests.Png();
        File.WriteAllBytes(source, bytes);
        var dds = DdsIconDecoderTests.CreateDds(CompressionFormat.Rgba);
        using var encoded = BarArchiveTests.BuildArchive(
            new BarArchiveTests.TestEntry("resources\\greek\\old.dds", dds, dds.Length, false),
            new BarArchiveTests.TestEntry("resources\\nested\\renamed.dds", dds, dds.Length, false));
        var archive = fixture.Workspace.GetPath("OriginalIcons.bar");
        var archiveBytes = encoded.ToArray();
        File.WriteAllBytes(archive, archiveBytes);
        var originals = await IconCatalog.LoadSourcesAsync(archive);
        var preview = new IconPreviewService(() => archive, () => resources);
        var before = await preview.LoadAsync("greek/old.png");
        Assert.Equal(source, before!.ResolvedSource);

        await Task.Run(() => AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Call("InitializeEditorTabs");
            host.Call("BuildEditorPanel", "IconUnit", true);
            host.Set("_isReadOnly", false);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var manager = new IconManagerWindow(originals.Select(item => item.LogicalPath),
                CustomAssetCatalog.LoadIconPaths(resources), resources,
                (item, destination) => (Task<bool>)host.Call("MoveCustomIconAsync", item, destination)!, originals);
            _ = manager.ShowDialog(host);
            try
            {
                var old = Items(manager).Single(item => item.Name == "old.png");
                Assert.True(old.IsCustom);
                Assert.Equal(2, old.Source!.PhysicalSources.Count);
                var row = (Grid)Call(manager, "CreateRow", old)!;
                Assert.Contains(row.Children.OfType<TextBlock>(), text => text.Text!.Contains(extension.ToUpperInvariant()));
                var operation = (Task)Call(manager, "OpenMoveDialogAsync", old)!;
                var destination = await DialogAsync<AssetDestinationWindow>(manager);
                Field<TextBox>(destination, "_folder").Text = "nested";
                Field<TextBox>(destination, "_name").Text = "renamed";
                Call(destination, "RefreshPreview");
                Assert.EndsWith("nested\\renamed." + extension, Field<TextBlock>(destination, "_physicalPreview").Text);
                Assert.Equal("resources\\nested\\renamed.png", Field<TextBlock>(destination, "_xmlPreview").Text);
                Assert.True(Field<Button>(destination, "_confirm").IsEnabled);
                Call(destination, "Confirm");
                Assert.EndsWith("renamed.png", destination.Destination!.AbsolutePath);
                var confirmation = await DialogAsync<Prompt>(host);
                Assert.Equal("Rename/Move Icon", confirmation.PromptTitle);
                Call(confirmation, "ConfirmButton_Click", null!, new RoutedEventArgs());
                await operation.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(2, Items(manager).Count);
                var restored = Items(manager).Single(item => item.Name == "old.png");
                Assert.False(restored.IsCustom);
                Assert.All(restored.Source!.PhysicalSources, file => Assert.False(file.IsCustom));
                var moved = Items(manager).Single(item => item.Name == "renamed.png");
                Assert.True(moved.IsCustom);
                Assert.Equal(2, moved.Source!.PhysicalSources.Count);
                Assert.EndsWith("renamed." + extension, moved.Source.PreferredSource!.Path);
                var visible = Field<ListBox>(manager, "_itemsList").ItemsSource!.Cast<IconManagerItem>().ToList();
                Assert.Equal(moved, Assert.Single(visible));
                Field<TextBox>(manager, "_searchBox").Text = "";
                Field<ComboBox>(manager, "_filterComboBox").SelectedItem = "Original";
                Call(manager, "RefreshList");
                Assert.Equal(restored, Assert.Single(Field<ListBox>(manager, "_itemsList").ItemsSource!.Cast<IconManagerItem>()));
                Assert.Contains("1 original, 1 custom", Field<TextBlock>(manager, "_footerText").Text);
                Assert.False((bool)host.Call("HasPendingAssetMoveChanges")!);
                var adopted = (XDocument)typeof(ProtoEditorWindow).GetField("_modXmlDoc", Private)!.GetValue(host)!;
                Assert.Equal("resources\\nested\\renamed.png", adopted.Descendants("icon").Single().Value);
            }
            finally
            {
                foreach (var dialog in manager.OwnedWindows.ToArray()) dialog.Close();
                foreach (var dialog in host.OwnedWindows.OfType<Prompt>().ToArray()) dialog.Close();
                manager.Close();
                host.DisposeFixtureWindow();
            }
            var reopened = new IconManagerWindow(originals.Select(item => item.LogicalPath),
                CustomAssetCatalog.LoadIconPaths(resources), resources, originalSources: originals);
            try
            {
                Assert.Equal(2, Items(reopened).Count);
                Assert.False(Items(reopened).Single(item => item.Name == "old.png").IsCustom);
                Assert.True(Items(reopened).Single(item => item.Name == "renamed.png").IsCustom);
            }
            finally { reopened.Close(); }
        }));
        Assert.False(File.Exists(source));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(resources, "nested", "renamed." + extension)));
        var saved = XDocument.Load(fixture.ProtoPath);
        Assert.Equal("resources\\nested\\renamed.png", saved.Descendants("icon").Single().Value);
        Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
        var restoredPreview = await preview.LoadAsync("greek/old.png");
        Assert.Contains(archive, restoredPreview!.ResolvedSource);
        var movedPreview = await preview.LoadAsync("resources/nested/renamed.png");
        Assert.EndsWith("renamed." + extension, movedPreview!.ResolvedSource);
        Assert.Equal(before.PngBytes, movedPreview.PngBytes);
        Assert.Equal(archiveBytes, File.ReadAllBytes(archive));
    }

    [Theory]
    [InlineData("original")]
    [InlineData("pair")]
    public void ProtectedRowsExplainAndRefuseMoveBeforeDestinationOrCallback(string mode)
    {
        using var workspace = new TestWorkspace();
        var resources = workspace.CreateDirectory("resources");
        if (mode == "pair")
        {
            workspace.WriteText("resources/old.png", "png");
            workspace.WriteText("resources/old.dds", "dds");
        }
        var before = Directory.GetFiles(resources).ToDictionary(path => path, File.ReadAllBytes);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var called = false;
            var manager = new IconManagerWindow(["resources/old.png"], CustomAssetCatalog.LoadIconPaths(resources), resources,
                (_, _) => { called = true; return Task.FromResult(true); });
            manager.Show();
            try
            {
                var item = Assert.Single(Items(manager));
                var row = (Grid)Call(manager, "CreateRow", item)!;
                var reason = Assert.IsType<string>(ToolTip.GetTip(row));
                Assert.Contains(mode == "pair" ? "Both PNG and DDS" : "Original game", reason);
                if (mode == "pair") Assert.Contains(row.Children.OfType<TextBlock>(), text => text.Text!.Contains("PNG + DDS · move blocked"));
                var operation = (Task)Call(manager, "OpenMoveDialogAsync", item)!;
                var error = await DialogAsync<Prompt>(manager);
                Assert.Equal("Icon move blocked", error.PromptTitle);
                Assert.DoesNotContain(manager.OwnedWindows, window => window is AssetDestinationWindow);
                error.Close();
                await operation.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(called);
                Assert.Equal(item, Assert.Single(Items(manager)));
            }
            finally { foreach (var dialog in manager.OwnedWindows.ToArray()) dialog.Close(); manager.Close(); }
        });
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Theory]
    [InlineData("destination-cancel")]
    [InlineData("callback-declined")]
    public void CancelledMovesLeaveManagerSourcesAndFilesUnchanged(string outcome)
    {
        using var workspace = new TestWorkspace();
        var resources = workspace.CreateDirectory("resources");
        var source = workspace.WriteText("resources/old.dds", "unchanged DDS bytes");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var calls = 0;
            var manager = new IconManagerWindow([], CustomAssetCatalog.LoadIconPaths(resources), resources,
                (_, _) => { calls++; return Task.FromResult(false); });
            manager.Show();
            try
            {
                var item = Assert.Single(Items(manager));
                var operation = (Task)Call(manager, "OpenMoveDialogAsync", item)!;
                var destination = await DialogAsync<AssetDestinationWindow>(manager);
                if (outcome == "destination-cancel") destination.Close();
                else { Field<TextBox>(destination, "_name").Text = "renamed"; Call(destination, "Confirm"); }
                await operation.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(outcome == "destination-cancel" ? 0 : 1, calls);
                Assert.Same(item, Assert.Single(Items(manager)));
                Assert.Equal(source, item.Source!.PreferredSource!.Path);
            }
            finally { foreach (var dialog in manager.OwnedWindows.ToArray()) dialog.Close(); manager.Close(); }
        });
        Assert.Equal("unchanged DDS bytes", File.ReadAllText(source));
        Assert.Single(Directory.GetFiles(resources));
    }

    [Theory]
    [InlineData("png", "png")]
    [InlineData("png", "dds")]
    [InlineData("dds", "png")]
    [InlineData("dds", "dds")]
    [InlineData("xml", "xml")]
    public void DestinationRechecksLateCollisionsAndPreservesOtherAssetPathBehavior(string extension, string collision)
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("assets");
        AvaloniaTestHost.Run(() =>
        {
            var icon = extension != "xml";
            var dialog = new AssetDestinationWindow("Move", root, "assets", icon ? "resources" : "", "." + extension, "renamed", usePngIconXmlPath: icon);
            try
            {
                Assert.True(Field<Button>(dialog, "_confirm").IsEnabled);
                Assert.Equal("assets\\renamed." + extension, Field<TextBlock>(dialog, "_physicalPreview").Text);
                Assert.Equal(icon ? "resources\\renamed.png" : "renamed.xml", Field<TextBlock>(dialog, "_xmlPreview").Text);
                var competitor = workspace.WriteText("assets/renamed." + collision, "external file");
                Call(dialog, "Confirm");
                Assert.Null(dialog.Destination);
                Assert.Contains(icon ? "PNG or DDS" : "already exists", Field<TextBlock>(dialog, "_error").Text);
                Call(dialog, "RefreshPreview");
                Assert.False(Field<Button>(dialog, "_confirm").IsEnabled);
                Assert.Equal("external file", File.ReadAllText(competitor));
            }
            finally { dialog.Close(); }
        });
    }
}
