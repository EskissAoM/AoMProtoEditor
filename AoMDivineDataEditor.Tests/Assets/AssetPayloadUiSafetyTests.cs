using System.Buffers.Binary;
using System.Reflection;
using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetPayloadUiSafetyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void PreviewRefusalDoesNotDisableAuthoritativeUnitAnimationValidation(bool archived, bool invalidAnimation)
    {
        using var fixture = new BaselineSafetyFixture();
        var animation = invalidAnimation ? "MissingAnimation" : "Idle";
        File.WriteAllText(fixture.ProtoPath, $"<protomods><unit name='AnimationUnit'><animfile>nested.xml</animfile>" +
            $"<protoaction><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic><anim>{animation}</anim></protoaction></unit></protomods>");
        XElement tree = new("anim", "Idle");
        for (var depth = 0; depth < 257; depth++) tree = new XElement("submodel", tree);
        var xml = new XElement("animfile", tree).ToString(SaveOptions.DisableFormatting);
        string? artDirectory = null;
        if (archived)
        {
            artDirectory = fixture.Workspace.CreateDirectory("IsolatedGame/art");
            var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
            using var bar = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry("nested.xml", bytes, bytes.Length, false));
            File.WriteAllBytes(Path.Combine(artDirectory, "ArtTest.bar"), bar.ToArray());
        }
        else fixture.Workspace.WriteText("TestMod/game/art/nested.xml", xml);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            host.Show();
            try
            {
                host.Call("InitializeEditorTabs");
                host.Call("RefreshProtoActionMetadata", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["HandAttack"] = "Attack" });
                host.Call("LoadCurrentModAssetCatalogs");
                if (archived) await (Task)host.Call("LoadBaseGameAnimFileCatalogAsync", fixture.Workspace.GetPath("IsolatedGame/data/Data.bar"))!;
                var preview = archived
                    ? await AnimFileCatalog.LoadXmlResultAsync(artDirectory!, new AnimFileCatalogEntry("nested.xml", "ArtTest.bar"))
                    : await AnimFileCatalog.LoadCustomXmlResultAsync(fixture.Workspace.GetPath("TestMod/game/art"), "nested.xml");
                Assert.Null(preview.Value); Assert.Contains("node/depth limit", preview.Error!);
                host.Call("BuildEditorPanel", "AnimationUnit", true);
                ((TabControl)typeof(ProtoEditorWindow).GetField("_editorTabs", Private)!.GetValue(host)!).SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var loaded = await (Task<AnimFileAnimationNames?>)host.Call("LoadDirectAnimFileAnimationCatalogAsync", "nested.xml")!;
                Assert.NotNull(loaded); Assert.Equal("Idle", Assert.Single(loaded.UnitAnimations));
                host.Set("_currentUnitAnimationCatalogTask", Task.FromResult<AnimFileAnimationNames?>(loaded));
                Assert.NotEmpty(((IEnumerable)host.Call("EnumerateProtoActionAnimationEditors")!).Cast<object>());
                var check = (Task<bool>)host.Call("EnsureValidProtoActionAnimationsBeforeSaveAsync")!;
                if (invalidAnimation)
                {
                    Prompt? prompt = null;
                    for (var attempt = 0; attempt < 200 && prompt == null; attempt++)
                    {
                        prompt = host.OwnedWindows.OfType<Prompt>().SingleOrDefault();
                        if (prompt == null) await Task.Delay(10);
                    }
                    Assert.NotNull(prompt);
                    Assert.Contains("Invalid ProtoAction Animations", prompt.PromptTitle);
                    Assert.Contains("MissingAnimation", prompt.PromptText);
                    prompt.Close();
                }
                Assert.Equal(!invalidAnimation, await check.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { foreach (var child in host.OwnedWindows.ToArray()) child.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void PreviewTooltipExplainsTheLimitWithoutChangingSelectionOrDirtyState()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Test'><icon>resources/large.png</icon></unit></protomods>");
        var root = fixture.Workspace.CreateDirectory("TestMod/game/ui_myth/resources");
        var png = AssetCreateServiceTests.Png();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.MaxValue);
        var path = fixture.Workspace.GetPath("TestMod/game/ui_myth/resources/large.png");
        File.WriteAllBytes(path, png);
        var before = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            var preview = new IconPreviewControl(new IconPreviewService(() => null, () => root));
            host.Content = preview;
            host.Show();
            try
            {
                await preview.ShowAsync("resources/large.png");
                var tooltip = Assert.IsType<string>(ToolTip.GetTip(preview));
                Assert.Contains("dimensions", tooltip);
                Assert.Contains(path, tooltip);
                Assert.Equal("Unavailable", ((TextBlock)typeof(IconPreviewControl).GetField("_placeholder", Private)!.GetValue(preview)!).Text);
                Assert.False((bool)typeof(ProtoEditorWindow).GetField("_isDirty", Private)!.GetValue(host)!);
                var document = (System.Xml.Linq.XElement)typeof(ProtoEditorWindow).GetField("_modXmlRoot", Private)!.GetValue(host)!;
                Assert.Equal("resources/large.png", document.Descendants("icon").Single().Value);
                Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
                Assert.Equal(png, File.ReadAllBytes(path));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void XmlViewerAndDuplicateExplainFailuresAndPublishNothing(bool sound, bool duplicate)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.WriteText("unit.xml", "<r>long content</r>");
        var before = File.ReadAllBytes(source);
        var item = new AnimFileCatalogEntry("unit.xml", "Custom", true);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var manager = new AnimFileManagerWindow([item], _ => throw new InvalidOperationException("Use the result loader"),
                workspace.RootPath, profile: sound ? XmlAssetManagerProfile.SoundSet : XmlAssetManagerProfile.AnimFile,
                loadXmlResultAsync: _ => AssetPayloadReader.LoadLooseXmlAsync(source, new AssetPayloadPolicy(MaximumXmlBytes: 8)));
            manager.Show();
            try
            {
                var button = new Button();
                var operation = (Task)typeof(AnimFileManagerWindow).GetMethod(duplicate ? "DuplicateAsync" : "OpenViewerAsync", Private)!
                    .Invoke(manager, [item, button])!;
                Prompt? prompt = null;
                for (var attempt = 0; attempt < 200 && prompt == null; attempt++)
                {
                    prompt = manager.OwnedWindows.OfType<Prompt>().SingleOrDefault();
                    if (prompt == null) await Task.Delay(10);
                }
                Assert.NotNull(prompt);
                Assert.Contains(source, prompt.PromptText);
                Assert.Contains("limit", prompt.PromptText);
                Assert.False(button.IsEnabled);
                prompt.Close();
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(button.IsEnabled);
                Assert.Empty(manager.OwnedWindows);
                Assert.Equal(source, Assert.Single(Directory.GetFiles(workspace.RootPath)));
                Assert.Equal(before, File.ReadAllBytes(source));
            }
            finally { foreach (var child in manager.OwnedWindows.ToArray()) child.Close(); manager.Close(); }
        });
    }
}
