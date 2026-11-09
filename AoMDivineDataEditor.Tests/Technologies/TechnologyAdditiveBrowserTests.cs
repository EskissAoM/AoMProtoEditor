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
public sealed class TechnologyAdditiveBrowserTests
{
    [Fact]
    public void MainAndPinnedDocumentHeadersDistinguishOriginalModifiedAndCustomForUnitsAndTechnologies()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'><researchpoints>20</researchpoints></tech><tech name='Custom'><researchpoints>7</researchpoints></tech></techtreemods>");
        AvaloniaTestHost.Run(() =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_barXmlRoot", XElement.Parse("<proto><unit name='BaseUnit'/></proto>"));
                host.Set("_cachedBaseTechtreeDocuments", new List<XDocument> { XDocument.Parse("<techtree><tech name='A'><researchpoints>30</researchpoints></tech></techtree>") });
                host.Call("EnsureTechnologyView");
                var kindType = typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!;
                var states = (IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
                var checks = new List<(object State, string Expected)>();
                foreach (var main in new[] { true, false })
                    foreach (var test in new[]
                    {
                        (Kind: "Technologies", Modified: false, Name: "A", Title: "Tech: A (Original)"),
                        (Kind: "Technologies", Modified: true, Name: "A", Title: "Tech: A (Modified)"),
                        (Kind: "Technologies", Modified: true, Name: "Custom", Title: "Tech: Custom (Custom)"),
                        (Kind: "Units", Modified: false, Name: "BaseUnit", Title: "Unit: BaseUnit (Original)"),
                        (Kind: "Units", Modified: true, Name: "BaseUnit", Title: "Unit: BaseUnit (Modified)"),
                        (Kind: "Units", Modified: true, Name: "CustomUnit", Title: "Unit: CustomUnit (Custom)")
                    })
                    {
                        var state = host.Call("CreateDocumentTab", main, Enum.Parse(kindType, test.Kind), test.Modified, test.Name)!;
                        states.Add(state); checks.Add((state, test.Title));
                    }
                host.Call("RefreshDocumentTabHeaders");
                foreach (var (state, expected) in checks)
                {
                    var title = (TextBlock)state.GetType().GetProperty("TitleText")!.GetValue(state)!;
                    var tab = (TabStripItem)state.GetType().GetProperty("TabItem")!.GetValue(state)!;
                    Assert.Equal(expected, title.Text);
                    Assert.StartsWith(expected, ToolTip.GetTip(tab)?.ToString());
                }
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Fact]
    public void SharedBrowserGroupsCustomThenModifiedAndKeepsTechnologyCollapseStateAcrossSearch()
    {
        using var fixture = new BaselineSafetyFixture();
        fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml",
            "<techtreemods><tech name='A'><researchpoints>20</researchpoints></tech><tech name='Custom'><researchpoints>7</researchpoints></tech></techtreemods>");
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_cachedBaseTechtreeDocuments", new List<XDocument> { XDocument.Parse("<techtree><tech name='A'><researchpoints>30</researchpoints></tech></techtree>") });
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Technologies");
                host.Call("ApplyEntityKindUi", kind);
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("RefreshUnitList"); host.Show(); await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                var list = host.FindControl<ListBox>("_unitList")!;
                string? Name(object row) => (string?)row.GetType().GetProperty("UnitName")!.GetValue(row);
                string? Label(object row) => (string?)row.GetType().GetProperty("DisplayName")!.GetValue(row);
                Assert.Equal(new[] { "Custom", "Custom", "Modified", "A" }, list.Items.Cast<object>().Select(Label));
                var view = (TechnologyEditorView)typeof(ProtoEditorWindow).GetField("_technologyView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
                view.SelectTechnology("A"); host.Call("RestoreEntityBrowserSelection");
                Assert.False(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified);
                Assert.DoesNotContain(list.Items.Cast<object>(), row => Name(row) == "A");
                host.FindControl<TextBox>("_searchBox")!.Text = "A"; host.Call("FilterUnitList");
                Assert.Single(list.Items); Assert.Equal("Modified", Label(list.Items[0]!));
                host.FindControl<TextBox>("_searchBox")!.Text = ""; host.Call("FilterUnitList");
                Assert.Equal(3, list.Items.Count);
                host.Call("ToggleUnitGroup", AdditiveEntryKind.Modified); Assert.Equal(4, list.Items.Count);
                view.SelectTechnology("Custom"); host.Call("RestoreEntityBrowserSelection");
                Assert.True(host.FindControl<Button>("_entityDeleteButton")!.IsEnabled);
                Assert.False(view.IsDirty);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
