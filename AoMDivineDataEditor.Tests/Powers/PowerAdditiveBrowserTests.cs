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

[Trait("Category", TestCategories.Ui)]
public sealed class PowerAdditiveBrowserTests
{
    [Fact]
    public void SharedPowerBrowserGroupsPartialOriginalsAndLabelsPinnedTabsByIdentity()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/god_powers/fixture.godpowers", "<powers><power name='A' type='GodPower'><activetime>10</activetime></power></powers>");
        fixture.Workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='A'><activetime>20</activetime></power><power name='Custom' type='GodPower'/><power name='Ability' type='UnitAction'/></powersmod>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Powers");
                host.Call("ApplyEntityKindUi", kind); host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("RefreshUnitList"); host.Show(); Dispatcher.UIThread.RunJobs();
                var view = (PowerEditorView)typeof(ProtoEditorWindow).GetField("_powerView", flags)!.GetValue(host)!;
                var list = host.FindControl<ListBox>("_unitList")!;
                string? Label(object row) => (string?)row.GetType().GetProperty("DisplayName")!.GetValue(row);
                Assert.Equal(new[] { "Custom", "Custom", "Modified", "A" }, list.Items.Cast<object>().Select(Label));
                await view.SelectPowerAsync("A"); host.Call("RestoreEntityBrowserSelection");
                Assert.Equal("20", view.FindControl<TextBox>("_activeTimeBox")!.Text);
                Assert.False(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified);
                host.FindControl<TextBox>("_searchBox")!.Text = "A"; host.Call("FilterUnitList");
                Assert.Single(list.Items); Assert.Equal("Modified", Label(list.Items[0]!));
                host.FindControl<TextBox>("_searchBox")!.Text = ""; host.Call("FilterUnitList"); Assert.Equal(3, list.Items.Count);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified); Assert.Equal(4, list.Items.Count);
                var tabs = (IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", flags)!.GetValue(host)!;
                foreach (var test in new[] { (Name: "A", Mod: true, Source: "Modified"), (Name: "A", Mod: false, Source: "Original"), (Name: "Custom", Mod: true, Source: "Custom") })
                {
                    var state = host.Call("CreateDocumentTab", false, kind, test.Mod, test.Name)!; tabs.Add(state);
                    host.Call("RefreshDocumentTabHeaders");
                    var title = (TextBlock)state.GetType().GetProperty("TitleText")!.GetValue(state)!;
                    Assert.Equal($"Power: {test.Name} ({test.Source})", title.Text);
                }
                await view.SelectPowerAsync("Custom"); host.Call("RestoreEntityBrowserSelection");
                Assert.True(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled); Assert.False(view.IsDirty);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
