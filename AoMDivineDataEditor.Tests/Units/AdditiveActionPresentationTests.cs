using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AdditiveActionPresentationTests
{
    [Fact]
    public void UnitChangeCardsRouteAcrossTabsAndKeepDisabledDistinctWithoutDirtyingCustomOrOriginal()
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, """
            <protomods><unit name="A">
              <maxhitpoints mergeMode="replace">120</maxhitpoints><trainpoints mergeMode="replace">8</trainpoints>
              <flag mergeMode="remove">CreateUnitGroupAutomatically</flag><rechargetime mergeMode="replace">6</rechargetime>
              <rechargeincludetypes><unittype mergeMode="remove">AbstractInfantry</unittype></rechargeincludetypes>
              <protoaction><name>HandAttack</name><rof mergeMode="replace">1.5</rof><handlogic>0</handlogic>
                <damagebonus type="AbstractCavalry" mergeMode="remove"/></protoaction>
              <command mergeMode="remove">Stop</command><train mergeMode="remove">Hoplite</train><tech mergeMode="remove">HandAxe</tech>
            </unit><unit name="Custom"><trainpoints>7</trainpoints></unit></protomods>
            """);
        var source = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var original = ProtoDataExtractor.ExtractProtoData("""
                    <proto><unit name="A"><maxhitpoints>100</maxhitpoints><trainpoints>10</trainpoints><rechargetime>4</rechargetime>
                      <flag>CreateUnitGroupAutomatically</flag><command>Stop</command><train>Hoplite</train><tech>HandAxe</tech>
                      <rechargeincludetypes><unittype>AbstractInfantry</unittype></rechargeincludetypes>
                      <protoaction><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic><rof>1</rof>
                        <damagebonus type="AbstractCavalry">1.25</damagebonus></protoaction>
                    </unit></proto>
                    """);
                host.Set("_barXmlRoot", original.Root); host.Set("_barData", original.Data);
                host.Call("RefreshProtoActionMetadata", (object)null!); host.Call("InitializeEditorTabs");
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1; host.Call("BuildEditorPanel", "A", true); host.Show();
                await Task.Delay(50); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
                string[] names = ["_unitStatsNotices", "_unitActionNotices", "_unitCommandNotices", "_unitAbilityNotices", "_unitTrainResearchNotices"];
                StackPanel Panel(int index) => host.FindControl<StackPanel>(names[index])!;
                Expander Card(int index, string header) => Assert.Single(Panel(index).GetLogicalDescendants().OfType<Expander>(),
                    card => Assert.IsType<TextBlock>(card.Header).Text == header);
                Card(0, "Replaced (3)"); Card(0, "Removed (2)");
                Card(1, "Replaced (1)"); Card(1, "Removed (1)"); var disabled = Card(1, "Disabled (1)");
                Card(2, "Removed (1)"); Card(3, "Replaced (1)"); Card(4, "Removed (2)");
                var rechargeRemoval = Card(3, "Removed (1)");
                Assert.Equal("Recharge include types → Unit types → Abstract infantry", Assert.Single(Assert.IsType<StackPanel>(rechargeRemoval.Content).Children.OfType<TextBlock>()).Text);
                Assert.All(names.SelectMany(name => host.FindControl<StackPanel>(name)!.GetLogicalDescendants().OfType<Expander>()), card => Assert.False(card.IsExpanded));
                Assert.Null(Assert.Single(Assert.IsType<StackPanel>(disabled.Content).Children.OfType<TextBlock>()).TextDecorations);
                var removedAction = Card(1, "Removed (1)");
                Assert.Null(Assert.Single(Assert.IsType<StackPanel>(removedAction.Content).Children.OfType<TextBlock>()).TextDecorations);
                Card(0, "Replaced (3)").IsExpanded = true;
                host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 1; host.UpdateLayout();
                Assert.False(Card(1, "Replaced (1)").IsExpanded);
                host.Call("BuildEditorPanel", "Custom", false); await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                Assert.All(names, name => Assert.Empty(host.FindControl<StackPanel>(name)!.Children));
                Assert.True(host.FindControl<Button>("_editUnitXmlButton")!.IsEnabled);
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 0; host.Call("BuildEditorPanel", "A", false);
                await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                Assert.All(names, name => Assert.Empty(host.FindControl<StackPanel>(name)!.Children));
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1; host.Call("BuildEditorPanel", "A", false);
                await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                Assert.True(Card(0, "Replaced (3)").IsExpanded); Assert.False(Card(1, "Replaced (1)").IsExpanded);
                Assert.False((bool)typeof(Windows.ProtoEditorWindow).GetField("_isDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!);
                Assert.Equal(source, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    [Theory]
    [InlineData("<handlogic>0</handlogic>", "<handlogic>0</handlogic>", null, false)]
    [InlineData("", "<rof>0</rof><maxrange>0</maxrange>", null, false)]
    [InlineData("", "<linearshockwave>0</linearshockwave>", null, false)]
    [InlineData("", "<active>0</active>", null, true)]
    [InlineData("", "<handlogic>0</handlogic>", "0", true)]
    public void DisabledSummaryUsesOriginalEffectiveFlagsAndNeverNumericZero(string originalFlags, string patchFields, string? modTacticsValue, bool expected)
    {
        using var fixture = new BaselineSafetyFixture();
        File.WriteAllText(fixture.ProtoPath, $"<protomods><unit name='A'><protoaction><name>HandAttack</name>{patchFields}</protoaction></unit></protomods>");
        const string tactics = "<tactics><action><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic></action></tactics>";
        fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/tactics/probe.tactics", tactics);
        if (modTacticsValue != null)
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/tactics/probe.tactics", tactics.Replace("<handlogic>1", "<handlogic>" + modTacticsValue));
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var extracted = ProtoDataExtractor.ExtractProtoData($"<proto><unit name='A'><tactics>probe.tactics</tactics><protoaction><name>HandAttack</name><type>Attack</type>{originalFlags}</protoaction></unit></proto>");
                host.Set("_barData", extracted.Data); host.Set("_barXmlRoot", extracted.Root);
                host.Call("RefreshProtoActionMetadata", (object)null!); host.Call("InitializeEditorTabs");
                host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("BuildEditorPanel", "A", true); host.Show();
                await Task.Delay(30); Dispatcher.UIThread.RunJobs();
                var panel = host.FindControl<StackPanel>("_unitActionNotices")!;
                Assert.Equal(expected, panel.IsVisible);
                var cards = panel.GetLogicalDescendants().OfType<Expander>().ToArray();
                Assert.Equal(expected ? 1 : 0, cards.Length);
                if (expected)
                {
                    Assert.Equal("Disabled (1)", Assert.IsType<TextBlock>(cards[0].Header).Text);
                    Assert.False(cards[0].IsExpanded);
                }
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }

    private static T Field<T>(object owner, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        return (T)(owner.GetType().GetField(name, flags)?.GetValue(owner) ?? owner.GetType().GetProperty(name, flags)!.GetValue(owner))!;
    }
    private static string Text(Control editor) => editor switch
    { TextBox box => box.Text ?? "", AutoCompleteBox box => box.Text ?? "", ComboBox box => box.SelectedItem?.ToString() ?? "", _ => "" };

    [Fact]
    public void DisabledHandLogicKeepsStoredRofVisibleAndStructuredOverridesKeepBothIndicators()
    {
        using var fixture = new BaselineSafetyFixture();
        // Same operations as ADM_Additive_Merge_Probe, with a minimal deterministic original/tactics fixture.
        File.WriteAllText(fixture.ProtoPath, "<protomods><unit name='Hoplite'><protoaction><name>HandAttack</name><rof>1.5</rof><handlogic>0</handlogic><minrange>0.5</minrange></protoaction></unit><unit name='VillagerGreek'><protoaction><name>Gather</name><rate type='WoodResource'>0.321</rate></protoaction></unit></protomods>");
        fixture.Workspace.WriteXml("IsolatedGame/data/gameplay/tactics/probe.tactics", "<tactics><action><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic><rof>1</rof></action><action><name>Gather</name><type>Gather</type><rate type='WoodResource'>1</rate><rate type='GoldResource'>1</rate></action></tactics>");
        var source = File.ReadAllBytes(fixture.ProtoPath);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var host = fixture.CreateHost(false);
            try
            {
                var extracted = ProtoDataExtractor.ExtractProtoData("<protos><proto><unit name='Hoplite'><tactics>probe.tactics</tactics><protoaction><name>HandAttack</name><type>Attack</type><rof>1</rof><maxrange>0.75</maxrange></protoaction></unit><unit name='VillagerGreek'><tactics>probe.tactics</tactics><protoaction><name>Gather</name><type>Gather</type><rate type='WoodResource'>1</rate><rate type='GoldResource'>0.85</rate></protoaction></unit></proto></protos>");
                host.Set("_barXmlRoot", extracted.Root); host.Set("_barData", extracted.Data);
                host.Call("RefreshProtoActionMetadata", (object)null!);
                host.Call("InitializeEditorTabs"); host.FindControl<TabStrip>("_unitTabs")!.SelectedIndex = 1;
                host.Call("BuildEditorPanel", "Hoplite", true); host.Show();
                host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 1;
                await Task.Delay(40); Dispatcher.UIThread.RunJobs();
                object Action(string name) => ((IEnumerable)typeof(Windows.ProtoEditorWindow).GetField("_protoActionWidgets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!).Cast<object>().Single(s => Field<ProtoAction>(s, "Model").Name == name);
                var attack = Action("HandAttack");
                Field<Button>(attack, "ActionSelectorButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20); Dispatcher.UIThread.RunJobs();
                var rof = Field<TextBox>(attack, "RofTb");
                Assert.True(rof.IsEffectivelyVisible); Assert.Equal("1.5", rof.Text);
                Assert.Contains("mod-changed", rof.Classes);
                Assert.DoesNotContain("handlogic", Field<HashSet<string>>(attack, "SelectedFlagTags"));
                var disabled = Assert.Single(host.FindControl<StackPanel>("_unitActionNotices")!.GetLogicalDescendants().OfType<Expander>());
                var disabledRow = Assert.Single(Assert.IsType<StackPanel>(disabled.Content).Children.OfType<TextBlock>());
                Assert.Equal("Disabled (1)", Assert.IsType<TextBlock>(disabled.Header).Text);
                Assert.Contains("HandAttack", disabledRow.Text);
                Assert.Null(disabledRow.TextDecorations);
                host.Call("BuildEditorPanel", "VillagerGreek", false);
                host.FindControl<TabControl>("_editorTabs")!.SelectedIndex = 1;
                await Task.Delay(40); Dispatcher.UIThread.RunJobs();
                var gather = Action("Gather");
                Field<Button>(gather, "ActionSelectorButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20); Dispatcher.UIThread.RunJobs();
                Control Rate(string type)
                {
                    var rows = (IEnumerable)Field<IDictionary>(gather, "StructuredFieldRows")["rate"]!;
                    var row = rows.Cast<object>().Single(r => Text(Field<Dictionary<string, Control>>(r, "AttributeEditors")["type"]) == type);
                    return Field<Control>(row, "ValueTb");
                }
                void AssertIndicators()
                {
                    var wood = Rate("WoodResource"); var gold = Rate("GoldResource");
                    Assert.Contains("mod-changed", wood.Classes); Assert.DoesNotContain("mod-changed", gold.Classes);
                    Assert.Equal("Original: 1\nModified: 0.321", ToolTip.GetTip(wood));
                    foreach (var value in new[] { wood, gold })
                    {
                        var hostGrid = Field<IDictionary>(gather, "SourceMarkerHosts")[value];
                        Assert.NotNull(hostGrid);
                        Assert.Contains(((Grid)hostGrid!).Children.OfType<Border>(), b => b.Background is ISolidColorBrush c && c.Color == Color.Parse("#f59e0b"));
                    }
                    Assert.Contains(wood.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_BorderElement" && b.BorderBrush is ISolidColorBrush c && c.Color == Color.Parse("#A78BFA"));
                }
                AssertIndicators();
                host.Call("RefreshProtoActionMetadataPanels", gather); Dispatcher.UIThread.RunJobs();
                AssertIndicators();
                Assert.Equal(source, File.ReadAllBytes(fixture.ProtoPath));
            }
            finally { host.DisposeFixtureWindow(); }
        });
    }
}
