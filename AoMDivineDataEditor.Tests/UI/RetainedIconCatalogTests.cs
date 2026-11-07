using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class RetainedIconCatalogTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)
        (owner is ProtoEditorWindow ? typeof(ProtoEditorWindow) : owner.GetType()).GetField(name, Private)!.GetValue(owner)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Theory]
    [InlineData("Technology", "clean")]
    [InlineData("Technology", "form")]
    [InlineData("Technology", "raw")]
    [InlineData("Power", "clean")]
    [InlineData("Power", "form")]
    [InlineData("Power", "raw")]
    [InlineData("MajorGod", "clean")]
    [InlineData("MajorGod", "form")]
    [InlineData("MajorGod", "raw")]
    public void ManagerReturnRefreshesPhysicalIconsWithoutReplacingRetainedDraft(string kind, string mode)
    {
        using var fixture = new BaselineSafetyFixture();
        var path = kind == "MajorGod" ? fixture.GodPath : fixture.Workspace.WriteText(
            "TestMod/game/data/gameplay/" + (kind == "Technology" ? "techtree_mods.xml" : "powers_mods.xml"),
            kind == "Technology" ? "<techtreemods><tech name='A'><icon>old.png</icon><researchpoints>1</researchpoints><future keep='yes'/></tech></techtreemods>"
            : "<powersmod><power name='P' type='TempUnit'><icon>old.png</icon><activetime>1</activetime><future keep='yes'/></power></powersmod>");
        if (kind == "MajorGod")
        {
            File.WriteAllText(path, "<civsmods><civ><name>CheckpointGod</name><culture>Greek</culture><icon>old.png</icon><future keep='yes'/></civ></civsmods>");
        }
        var obsolete = fixture.Workspace.WriteText("TestMod/game/ui_myth/resources/obsolete.png", "unused catalog file");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_baseGameIconPaths", new[] { "old.png", "other.png" });
                host.Call("LoadCurrentModAssetCatalogs");
                Control view;
                if (kind == "Technology")
                {
                    host.Call("EnsureTechnologyView");
                    var tech = Field<TechnologyEditorView>(host, "_technologyView");
                    tech.SetModifiedMode(true); tech.SelectTechnology("A");
                    await (Task)typeof(TechnologyEditorView).GetMethod("AwaitActiveEditorBuildCompletionAsync", Private)!.Invoke(tech, null)!;
                    view = tech;
                }
                else if (kind == "Power")
                {
                    host.Call("EnsurePowerView");
                    var power = Field<PowerEditorView>(host, "_powerView");
                    power.SetModifiedMode(true); await power.SelectPowerAsync("P"); view = power;
                }
                else
                {
                    // Fixture constructs this view before the initial catalog load.
                    host.God!.UpdateIconCatalog(["old.png", "other.png", "obsolete.png"]);
                    host.God.SetModifiedMode(true); host.God.SelectMajorGod("CheckpointGod"); view = host.God;
                }
                Dispatcher.UIThread.RunJobs();
                var asset = view.GetLogicalDescendants().OfType<AssetPathEditor>().Single(editor => editor.FullValue == "old.png");
                Assert.Contains(asset.Suggestions, item => item.FullValue.EndsWith("obsolete.png"));
                var raw = view.FindControl<TextEditor>(kind == "Technology" ? "_xmlPreview" : "_rawXmlEditor")!;
                bool Dirty() => view switch { TechnologyEditorView t => t.IsDirty, PowerEditorView p => p.IsDirty, MajorGodEditorView g => g.IsDirty, _ => throw new InvalidOperationException() };
                Assert.False(Dirty());
                if (mode == "form") asset.Editor.SelectedItem = asset.Suggestions.Single(item => item.FullValue == "other.png");
                if (mode == "raw")
                {
                    view.FindControl<Button>("_editXmlButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var i = 0; raw.IsReadOnly && i < 100; i++) await Task.Delay(10);
                    Assert.False(raw.IsReadOnly); raw.Text += "<!-- pending raw draft -->";
                }
                var before = File.ReadAllBytes(path); var text = raw.Text; var value = asset.FullValue;
                Assert.Equal(mode != "clean", Dirty());
                File.Delete(obsolete);
                var importedPath = fixture.Workspace.WriteText("TestMod/game/ui_myth/resources/imported.png", "");
                File.WriteAllBytes(importedPath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
                host.Call("ReloadIconCatalogsAfterManager");
                host.Call("ReloadIconCatalogsAfterManager");
                Dispatcher.UIThread.RunJobs();
                Assert.Same(asset, view.GetLogicalDescendants().OfType<AssetPathEditor>().Single(editor => ReferenceEquals(editor, asset)));
                Assert.Equal(value, asset.FullValue); Assert.Equal(text, raw.Text);
                Assert.Equal(mode != "clean", Dirty()); Assert.Equal(before, File.ReadAllBytes(path));
                Assert.DoesNotContain(asset.Suggestions, item => item.FullValue.EndsWith("obsolete.png"));
                var imported = Assert.Single(asset.Suggestions, item => item.FullValue.EndsWith("imported.png"));
                if (mode == "clean")
                {
                    asset.Editor.SelectedItem = imported;
                    Assert.True(Dirty());
                    Task<bool> saving = view switch { TechnologyEditorView t => t.SaveAsync(), PowerEditorView p => p.SaveAsync(), MajorGodEditorView g => g.SaveAsync(), _ => throw new InvalidOperationException() };
                    Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.EndsWith("imported.png", XDocument.Load(path).Descendants("icon").Single().Value);
                    Assert.False(Dirty());
                    // Reopen the saved definition and require the same refreshed membership.
                    if (view is TechnologyEditorView technology) { technology.SelectTechnology("A"); await (Task)typeof(TechnologyEditorView).GetMethod("AwaitActiveEditorBuildCompletionAsync", Private)!.Invoke(technology, null)!; }
                    else if (view is PowerEditorView powerEditor) await powerEditor.SelectPowerAsync("P");
                    else ((MajorGodEditorView)view).SelectMajorGod("CheckpointGod");
                    Assert.Contains(view.GetLogicalDescendants().OfType<AssetPathEditor>(), editor => editor.FullValue.EndsWith("imported.png"));
                }
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
