using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed class AdditiveCardNavigationTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();

    [Theory]
    [InlineData("Units", "_unitStatsNotices")]
    [InlineData("Technologies", "_technologyPropertyNotices")]
    [InlineData("Powers", "_powerPropertyNotices")]
    [InlineData("MajorGods", "_majorGodPropertyNotices")]
    public void BrowserEntryOpensCollapsedWhilePinnedDocumentsKeepIndependentState(string family, string panelName)
    {
        using var fixture = new BaselineSafetyFixture();
        const string flag = "<flag>CountsTowardEconomicScore</flag>";
        if (family == "Units")
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/proto_mods.xml", "<protomods><unit name='A'><flag mergeMode='remove'>Hero</flag></unit><unit name='B'><flag mergeMode='remove'>Hero</flag></unit></protomods>");
        else if (family == "Technologies")
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/techtree_mods.xml", "<techtreemods><tech name='A'><flag mergeMode='remove'>CountsTowardEconomicScore</flag></tech><tech name='B'><flag mergeMode='remove'>CountsTowardEconomicScore</flag></tech></techtreemods>");
        else if (family == "Powers")
        {
            fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/god_powers/fixture.godpowers", "<powers><power name='A' type='GodPower'><activetime>10</activetime></power><power name='B' type='GodPower'><activetime>10</activetime></power></powers>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='A'><activetime mergeMode='replace'>20</activetime></power><power name='B'><activetime mergeMode='replace'>30</activetime></power></powersmod>");
        }
        else
        {
            fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/major_gods.xml", "<civs><civ><name>A</name><buildingefficiency>0.6</buildingefficiency></civ><civ><name>B</name><buildingefficiency>0.6</buildingefficiency></civ></civs>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/major_gods_mods.xml", "<civsmods><civ><name>A</name><buildingefficiency mergeMode='replace'>0.7</buildingefficiency></civ><civ><name>B</name><buildingefficiency mergeMode='replace'>0.8</buildingefficiency></civ></civsmods>");
        }
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                host.Set("_barXmlRoot", XElement.Parse("<proto><unit name='A'><flag>Hero</flag></unit><unit name='B'><flag>Hero</flag></unit></proto>"));
                host.Set("_cachedBaseTechtreeDocuments", new List<XDocument> { XDocument.Parse($"<techtree><tech name='A'>{flag}</tech><tech name='B'>{flag}</tech></techtree>") });
                var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, family);
                host.Call("ApplyEntityKindUi", kind); host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("RefreshUnitList"); host.Show();
                async Task Settle()
                {
                    if (family == "Technologies")
                    {
                        var view = typeof(ProtoEditorWindow).GetField("_technologyView", Private)!.GetValue(host)!;
                        await (Task)typeof(TechnologyEditorView).GetField("_activeEditorBuildTask", Private)!.GetValue(view)!;
                    }
                    else if (family == "Powers")
                    {
                        var view = typeof(ProtoEditorWindow).GetField("_powerView", Private)!.GetValue(host)!;
                        await (Task)typeof(PowerEditorView).GetField("_populationTask", Private)!.GetValue(view)!;
                    }
                    else if (family == "MajorGods")
                    {
                        var view = typeof(ProtoEditorWindow).GetField("_majorGodView", Private)!.GetValue(host)!;
                        await (Task)typeof(MajorGodEditorView).GetField("_populationTask", Private)!.GetValue(view)!;
                    }
                    await Task.Delay(30); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                }
                async Task Browse(string name)
                {
                    var list = host.FindControl<ListBox>("_unitList")!;
                    var row = list.Items.Cast<object>().Single(item => (string?)item.GetType().GetProperty("UnitName")!.GetValue(item) == name);
                    host.Set("_suppressUnitListSelectionChange", true);
                    list.SelectedItem = row;
                    host.Set("_suppressUnitListSelectionChange", false);
                    await (Task)host.Call("RunUnitListSelectionChangeAsync")!; await Settle();
                }
                Control Editor() => family == "Units" ? host : (Control)typeof(ProtoEditorWindow)
                    .GetField(family == "Technologies" ? "_technologyView" : family == "MajorGods" ? "_majorGodView" : "_powerView", Private)!.GetValue(host)!;
                Expander Card() => Assert.Single(Editor().FindControl<StackPanel>(panelName)!.GetLogicalDescendants().OfType<Expander>());
                await Browse("A"); Assert.False(Card().IsExpanded); Card().IsExpanded = true;
                await Browse("B"); Assert.False(Card().IsExpanded); Card().IsExpanded = true;
                await Browse("A"); Assert.False(Card().IsExpanded); Card().IsExpanded = true;
                var pinnedA = host.Call("CreateDocumentTab", false, kind, true, "A")!;
                ((IList)typeof(ProtoEditorWindow).GetField("_openDocumentTabs", Private)!.GetValue(host)!).Add(pinnedA);
                await (Task)host.Call("ActivateDocumentTabAsync", pinnedA)!; await Settle(); Assert.True(Card().IsExpanded);
                await Browse("B"); Assert.False(Card().IsExpanded); // Browser opening resets B, but leaves pinned A alone.
                await (Task)host.Call("ActivateDocumentTabAsync", pinnedA)!; await Settle(); Assert.True(Card().IsExpanded);
                var tabControl = family switch
                {
                    "Units" => host.FindControl<TabControl>("_editorTabs")!,
                    "Technologies" => Editor().FindControl<TabControl>("_technologyEditorTabs")!,
                    "MajorGods" => Editor().FindControl<TabControl>("_majorGodTabs")!,
                    _ => Editor().FindControl<TabControl>("_powerTabs")!
                };
                tabControl.SelectedIndex = 1; tabControl.SelectedIndex = 0; await Settle(); Assert.True(Card().IsExpanded);
                Assert.False((bool)typeof(ProtoEditorWindow).GetField("_isDirty", Private)!.GetValue(host)!);
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
