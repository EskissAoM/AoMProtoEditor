using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TechnologyEditorWorkflowTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void GeneratedTransformTechnologyFocusAndNavigationPreserveCleanState(bool metadata, bool legacyFormatting)
    {
        var xml = "<techtreemods>" + GeneratedTowerTechnology(metadata, legacyFormatting) + "<tech name='Other'><researchpoints>1</researchpoints></tech></techtreemods>";
        using var fixture = new TechnologyEditorFixture(xml);
        var before = File.ReadAllBytes(fixture.TechtreePath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true);
                for (var turn = 0; turn < 2; turn++)
                {
                    editor.SelectTechnology("MilitaryMineToMilitaryCampTower");
                    await AwaitTechnologyBuild(editor);
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                    foreach (var box in editor.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEnabled && box.IsEffectivelyVisible).ToArray())
                    { box.Focus(); await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); }
                    Assert.True(await editor.CommitCurrentTechnologyAsync());
                    Assert.False(editor.IsDirty, Raw(editor).Text);
                    Assert.False(editor.IsTechnologyDirty("MilitaryMineToMilitaryCampTower"));
                    editor.SelectTechnology("Other"); await AwaitTechnologyBuild(editor);
                    Assert.False(editor.IsDirty);
                }
                Assert.Equal(before, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }

    private static string GeneratedTowerTechnology(bool metadata, bool legacyFormatting)
        => "<tech name='MilitaryMineToMilitaryCampTower'" + (metadata ? " type='Normal' orderhint='0'" : "") + "><cost resourcetype='Wood'>" + (legacyFormatting ? "75.0000" : "75") + "</cost><cost resourcetype='Gold'>" + (legacyFormatting ? "75.0000" : "75") + "</cost><researchpoints>" + (legacyFormatting ? "15.0000" : "15") + "</researchpoints><status>UNOBTAINABLE</status><flag>HideAllNotifications</flag><techtype>ResearchableCommand</techtype><techtype>SocketTechnology</techtype><techtype>MilitaryCampSocket</techtype></tech>";

    [Fact]
    public void GeneratedTransformTechnologyActualBrowserClickDoesNotDirtyOrPrompt()
    {
        using var fixture = new BaselineSafetyFixture();
        var path = fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='A'/>" + GeneratedTowerTechnology(true, true) + "<tech name='Other'/></techtreemods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var (view, kind) = AttachXmlTechnologyHost(host);
                host.Call("ApplyEntityKindUi", kind); host.Call("RefreshUnitList");
                host.Width = 1440; host.Height = 932; await SettleXmlPointer();
                for (var turn = 0; turn < 2; turn++)
                {
                    await ClickXmlTechnology(host, "MilitaryMineToMilitaryCampTower");
                    await XmlHostField<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(3));
                    await AwaitTechnologyBuild(view); await SettleXmlPointer();
                    Assert.False(view.IsTechnologyDirty("MilitaryMineToMilitaryCampTower"), Raw(view).Text);
                    Assert.False(view.IsDirty);
                    await ClickXmlTechnology(host, "Other");
                    Assert.Empty(host.OwnedWindows.OfType<Prompt>());
                    await XmlHostField<Task>(host, "_unitSelectionChangeTask").WaitAsync(TimeSpan.FromSeconds(3));
                    Assert.Equal("Other", view.CurrentTechnologyName);
                }
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            finally { foreach (var popup in host.OwnedWindows.OfType<Prompt>().ToArray()) popup.Close(); host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FormattedTechnologyNumbersStillTrackSaveAndDiscardRealEdits(bool research)
    {
        using var fixture = new TechnologyEditorFixture("<techtreemods><tech name='A'><cost resourcetype='Wood'>75.0000</cost><researchpoints>15.0000</researchpoints><future>1.0000</future></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var editor = fixture.CreateEditor();
            await InXmlWindow(editor, async _ =>
            {
                editor.SetModifiedMode(true); editor.SelectTechnology("A"); await AwaitTechnologyBuild(editor);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.False(editor.IsDirty, Raw(editor).Text);
                var box = editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == (research ? "15" : "75"));
                box.Text = research ? "16" : "80";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(editor.IsDirty); Assert.True(editor.IsTechnologyDirty("A"));
                await SaveXmlChecked(editor);
                var saved = FindTechnology(XDocument.Load(fixture.TechtreePath), "A");
                Assert.Equal(research ? "16" : "15.0000", saved.Element("researchpoints")!.Value);
                Assert.Equal(research ? "75.0000" : "80", saved.Element("cost")!.Value);
                Assert.Equal("1.0000", saved.Element("future")!.Value);
                Assert.False(editor.IsDirty);
                var bytes = File.ReadAllBytes(fixture.TechtreePath);
                box = editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == (research ? "16" : "80"));
                box.Text = "90";
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(editor.IsDirty);
                editor.DiscardTechnologyChanges("A", editor.LoadSavedTechnologyElement("A"));
                await AwaitTechnologyBuild(editor);
                Assert.False(editor.IsDirty); Assert.Equal(bytes, File.ReadAllBytes(fixture.TechtreePath));
            });
        });
    }
}
