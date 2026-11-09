using System.Collections;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed class MajorGodAdditiveBrowserTests : IDisposable
{
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();
    [Fact]
    public void BrowserGroupsOriginalOverlaysAndCustomWithIndependentCollapseAndAccuratePinnedLabels()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/major_gods.xml", "<civs><defaultpoplimit>300</defaultpoplimit><civ><name>A</name><culture>Greek</culture><key>G</key></civ></civs>");
        fixture.Workspace.WriteXml("TestMod/game/data/gameplay/major_gods_mods.xml", "<civsmods><civ><name>A</name><key>Z</key></civ><civ><name>Custom</name><key>C</key></civ></civsmods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "MajorGods");
                host.Call("ApplyEntityKindUi", kind); host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("RefreshUnitList"); host.Show(); Dispatcher.UIThread.RunJobs();
                var list = host.FindControl<ListBox>("_unitList")!;
                string? Label(object row) => (string?)row.GetType().GetProperty("DisplayName")!.GetValue(row);
                Assert.Equal(new[] { "Custom", "Custom", "Modified", "A" }, list.Items.Cast<object>().Select(Label));
                host.God!.SelectMajorGod("A"); await (Task)typeof(MajorGodEditorView).GetField("_populationTask", flags)!.GetValue(host.God)!;
                host.Call("RestoreEntityBrowserSelection");
                Assert.False(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled);
                Assert.Equal("Greek", host.God.FindControl<AutoCompleteBox>("_cultureBox")!.Text);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified);
                host.FindControl<TextBox>("_searchBox")!.Text = "A"; host.Call("FilterUnitList");
                Assert.Single(list.Items); Assert.Equal("Modified", Label(list.Items[0]!));
                host.FindControl<TextBox>("_searchBox")!.Text = ""; host.Call("FilterUnitList");
                Assert.Equal(3, list.Items.Count); host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified);
                var states = (IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", flags)!.GetValue(host)!;
                foreach (var item in new[] { (Name: "A", Mod: true, Source: "Modified"), (Name: "A", Mod: false, Source: "Original"), (Name: "Custom", Mod: true, Source: "Custom") })
                {
                    var state = host.Call("CreateDocumentTab", false, kind, item.Mod, item.Name)!; states.Add(state);
                    host.Call("RefreshDocumentTabHeaders");
                    Assert.Equal($"Major God: {item.Name} ({item.Source})", ((TextBlock)state.GetType().GetProperty("TitleText")!.GetValue(state)!).Text);
                }
                host.God.SelectMajorGod("Custom"); host.Call("RestoreEntityBrowserSelection");
                Assert.True(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled);
                Assert.Equal(MajorGodCatalog.SharedDataDisplayName, host.God.GetMajorGodNames(false)[0]);
                Assert.False(host.God.IsDirty);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
