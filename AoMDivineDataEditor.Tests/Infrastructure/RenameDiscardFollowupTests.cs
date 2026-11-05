using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class RenameDiscardFollowupTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object owner, string name) => (T)(owner is BaselineHost ? typeof(ProtoEditorWindow) : owner.GetType())
        .GetField(name, Private)!.GetValue(owner)!;
    private static object Kind(string name) => Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, name);
    private static async Task Settle() { await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(30); }
    private static string Name(object item) => (string)item.GetType().GetProperty("UnitName")!.GetValue(item)!;

    private static (string Path, object View) Setup(BaselineSafetyFixture fixture, BaselineHost host, bool power)
    {
        var path = power ? fixture.Workspace.WriteText("TestMod/game/data/gameplay/powers_mods.xml",
            "<powersmod><power name='Old' type='GeneralEffect' godpower=''/><power name='Other' type='GeneralEffect' godpower=''/></powersmod>") : fixture.GodPath;
        if (!power)
        {
            var id = MajorGodCatalog.BuildStringId("Old", "displaynameid");
            File.WriteAllText(path, $"<civsmods><civ><name>Old</name><statsid>SP</statsid><displaynameid>{id}</displaynameid><titleid>{id}_T</titleid><rollovernameid>{id}_LR</rollovernameid><key>A</key><culture>Greek</culture></civ><civ><name>Other</name><statsid>SP</statsid></civ></civsmods>");
            fixture.Workspace.WriteText("TestMod/game/data/strings/English/stringmods.txt",
                $"ID = \"{id}\" ; Str = \"Old display name\"\r\nID = \"{id}_T\" ; Str = \"Old title\"\r\nID = \"{id}_LR\" ; Str = \"Old rollover\"\r\n");
            // The fixture created its God view before this test's document existed.
            host.GodHost.Content = null; host.Set("_majorGodView", null!); host.Call("EnsureMajorGodView");
        }
        if (power) { host.GodHost.Content = null; host.Set("_powerView", null!); host.Call("EnsurePowerView"); }
        object view = power ? Field<PowerEditorView>(host, "_powerView") : host.God!;
        if (power) ((PowerEditorView)view).SetModifiedMode(true); else ((MajorGodEditorView)view).SetModifiedMode(true);
        return (path, view);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DiscardRestoresTypedOrCommittedRenameAndLeavesNoDirtyOwner(bool power, bool commit)
    {
        using var fixture = new BaselineSafetyFixture();
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var (path, view) = Setup(fixture, host, power); var before = File.ReadAllBytes(path);
                Assert.Contains("Old", power ? ((PowerEditorView)view).GetPowerNames(true) : ((MajorGodEditorView)view).GetMajorGodNames(true));
                if (power) await ((PowerEditorView)view).SelectPowerAsync("Old"); else ((MajorGodEditorView)view).SelectMajorGod("Old");
                await Settle();
                var saved = power ? ((PowerEditorView)view).LoadSavedPowerElement("Old") : ((MajorGodEditorView)view).LoadSavedMajorGodElement("Old");
                Field<TextBox>(view, "_nameBox").Text = "Renamed"; await Settle();
                if (commit) Assert.True(power ? await ((PowerEditorView)view).CommitCurrentPowerAsync() : await ((MajorGodEditorView)view).CommitCurrentMajorGodAsync());
                if (power) ((PowerEditorView)view).DiscardPowerChanges(commit ? "Renamed" : "Old", saved);
                else ((MajorGodEditorView)view).DiscardMajorGodChanges(commit ? "Renamed" : "Old", saved);
                await Settle();
                Assert.Equal(new[] { "Old", "Other" }, (power ? ((PowerEditorView)view).GetPowerNames(true) : ((MajorGodEditorView)view).GetMajorGodNames(true)).Order());
                Assert.False(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
                Assert.Equal("Old", power ? ((PowerEditorView)view).CurrentPowerName : ((MajorGodEditorView)view).CurrentMajorGodName);
                Assert.Equal("Old", Field<TextBox>(view, "_nameBox").Text);
                Assert.Equal(before, File.ReadAllBytes(path));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserCancelThenDiscardRestoresNamesSelectionAndReopening(bool power)
    {
        using var fixture = new BaselineSafetyFixture(); var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                var (path, view) = Setup(fixture, host, power); var before = File.ReadAllBytes(path);
                host.Call("InitializeEditorTabs"); var kind = Kind(power ? "Powers" : "MajorGods"); host.Set("_activeEntityKind", kind);
                host.Set("_documentTabActivationInProgress", true); Field<TabStrip>(host, "_unitTabs").SelectedIndex = 1; host.Set("_documentTabActivationInProgress", false);
                host.GodHost.Content = null; host.Content = (Control)view; host.Show();
                if (power) await ((PowerEditorView)view).SelectPowerAsync("Old"); else ((MajorGodEditorView)view).SelectMajorGod("Old");
                await Settle(); host.Call("SetMainDocumentTarget", kind, true, "Old"); host.Call("RefreshUnitList");
                if (!power) Field<TextBox>(view, "_nameBox").Focus();
                Field<TextBox>(view, "_nameBox").Text = "Renamed"; await Settle();
                var list = Field<ListBox>(host, "_unitList");
                async Task Navigate(string target, bool? discard)
                {
                    host.Set("_suppressUnitListSelectionChange", true);
                    list.SelectedItem = Field<IEnumerable>(host, "_filteredUnitNames").Cast<object>().Single(item => Name(item) == target);
                    host.Set("_suppressUnitListSelectionChange", false);
                    var task = (Task)host.Call("RunUnitListSelectionChangeAsync")!;
                    if (discard != null)
                    {
                        for (var attempt = 0; attempt < 100 && !host.OwnedWindows.OfType<Prompt>().Any() && !task.IsCompleted; attempt++) await Task.Delay(10);
                        var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>());
                        Assert.Equal("Unsaved main document", prompt.PromptTitle);
                        typeof(Prompt).GetProperty(nameof(Prompt.Confirmed))!.SetValue(prompt, discard.Value); prompt.Close();
                    }
                    await task.WaitAsync(TimeSpan.FromSeconds(5)); await Settle();
                }
                await Navigate("Other", false);
                // Power replacement now asks before committing a typed rename.
                // Cancel keeps the original identity selected and retains the draft text.
                Assert.Equal(power ? "Old" : "Renamed", Name(list.SelectedItem!));
                Assert.Equal("Renamed", Field<TextBox>(view, "_nameBox").Text);
                Assert.True(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
                Assert.Equal(before, File.ReadAllBytes(path));
                await Navigate("Other", true);
                Assert.Equal("Other", Name(list.SelectedItem!));
                Assert.Equal("Other", Field<TextBox>(view, "_nameBox").Text);
                Assert.False(power ? ((PowerEditorView)view).IsDirty : ((MajorGodEditorView)view).IsDirty);
                Assert.Equal(new[] { "Old", "Other" }, Field<IEnumerable>(host, "_filteredUnitNames").Cast<object>().Select(Name).Order());
                await Navigate("Old", null);
                Assert.Equal("Old", Field<TextBox>(view, "_nameBox").Text);
                Assert.True(Field<TextBox>(view, "_nameBox").IsEnabled);
                Assert.Equal("Old", Name(list.SelectedItem!));
                if (!power)
                {
                    Assert.False(((MajorGodEditorView)view).IsDirty,
                        "Reopening after discard must stay clean: " + string.Join(",", Field<HashSet<string>>(view, "_dirtyNames")));
                    await Navigate("Other", null);
                    Assert.False(((MajorGodEditorView)view).IsDirty);
                    Assert.Equal("Other", Name(list.SelectedItem!));
                }
                Assert.Empty(host.OwnedWindows); Assert.Equal(before, File.ReadAllBytes(path));
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateBaseUnitWithTransformNeverStagesSourceDeletion(bool renameAgain)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.GodPath, "<civsmods><civ><name>CheckpointGod</name><chainablebuilding>MilitaryCamp</chainablebuilding></civ></civsmods>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/proto_unit_command_mods.xml", "<protounitcommandmods><protounitcommand><name>MilitaryCampToTower</name><associatedtech>MilitaryCampToTower</associatedtech><transform/></protounitcommand></protounitcommandmods>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/unit_transform_mods.xml", "<unittransformmods><transform><command>MilitaryCampToTower</command><from>MilitaryCamp</from><to>MilitaryTower</to></transform></unittransformmods>");
        fixture.Workspace.WriteText("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='MilitaryCampToTower'/></techtreemods>");
        var host = AvaloniaTestHost.Run(() => fixture.CreateHost(false));
        try
        {
            AvaloniaTestHost.RunAsync(async () =>
            {
                host.Set("_barData", new ProtoBarData { UnitNames = ["MilitaryCamp", "MilitaryTower"] });
                host.Set("_barXmlRoot", XElement.Parse("<proto><unit name='MilitaryCamp'><command>MilitaryCampToTower</command></unit><unit name='MilitaryTower'/></proto>"));
                host.Call("InitializeEditorTabs"); host.Set("_activeEntityKind", Kind("Units"));
                host.Call("BuildEditorPanel", "MilitaryCamp", true); await Settle(); host.Show();
                var duplicate = (Task)host.Call("AddUnitAsync", true)!;
                var input = Assert.Single(host.OwnedWindows.OfType<InputPromptWindow>());
                typeof(InputPromptWindow).GetProperty(nameof(InputPromptWindow.InputText))!.SetValue(input, "MilitaryMine"); input.Close();
                await duplicate.WaitAsync(TimeSpan.FromSeconds(5)); await Settle();
                Assert.DoesNotContain("MilitaryCamp", Field<HashSet<string>>(host, "_dirtyUnitNames"));
                Assert.Contains("MilitaryMine", Field<HashSet<string>>(host, "_dirtyUnitNames"));
                if (renameAgain)
                {
                    ((TextBox)Field<IDictionary>(host, "_fieldControls")["__proto_unit_name"]!).Text = "MilitaryMine2";
                    await Settle();
                }
                Assert.Empty((IReadOnlyList<string>)host.Call("GetPendingDeletedUnitNames")!);
                var saving = (Task)host.Call("SaveCurrentUnitCoreAsync", null!, new Avalonia.Interactivity.RoutedEventArgs())!;
                if (await Task.WhenAny(saving, Task.Delay(3000)) != saving)
                {
                    var prompt = Assert.Single(host.OwnedWindows.OfType<Prompt>()); var message = prompt.PromptText; prompt.Close();
                    await saving; Assert.Fail(message);
                }
                await saving;
                var document = XDocument.Load(fixture.ProtoPath);
                Assert.Equal(renameAgain ? "MilitaryMine2" : "MilitaryMine", (string?)document.Root!.Element("unit")!.Attribute("name"));
                Assert.DoesNotContain(document.Root.Elements("unit"), unit => (string?)unit.Attribute("name") == "MilitaryCamp");
                Assert.Equal("MilitaryCamp", XDocument.Load(fixture.GodPath).Descendants("chainablebuilding").Single().Value);
            });
        }
        finally { AvaloniaTestHost.Run(host.DisposeFixtureWindow); }
    }
}
