using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class GodFieldPresentationTests : IDisposable
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MajorGodMainAndBonusRolloversStayBoundedAndClean(bool original)
    {
        using var workspace = new TestWorkspace();
        var source = Definition("Bonus"); source.Add(new XElement("rollovernameid", "STR_MAIN"));
        var goal = source.Element("bonusunitspawning")!.Elements().First();
        MajorGodCatalog.AddBonusDisplayInfo(goal).SetElementValue("rolloverid", "STR_BONUS");
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", new XElement("civsmods", source).ToString());
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView(original ? [new MajorGodDefinition("A", true, source)] : [], path,
                resolveStringAsync: _ => Task.FromResult<string?>("Description é with literal \\n"), cultureNames: ["Greek"], protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA"]);
            var window = new Window { Content = view, Width = 1440, Height = 950 };
            try
            {
                window.Show(); view.SetModifiedMode(!original); view.SelectMajorGod("A"); await Settle(window);
                var fields = view.GetLogicalDescendants().OfType<TextBox>().Where(EditorRolloverField.IsLong).Distinct().ToArray();
                Assert.Equal(2, fields.Length); Assert.All(fields, RolloverAssertions.Configured);
                foreach (var index in new[] { 0, 2 })
                foreach (var width in new[] { 1440d, 1100d })
                {
                    view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex = index;
                    window.Width = width; await Settle(window);
                    var tab = Assert.IsType<TabItem>(view.FindControl<TabControl>("_majorGodTabs")!.SelectedItem);
                    var scroll = Assert.IsType<ScrollViewer>(tab.Content);
                    FormLayoutAssertions.NoOverlaps(Assert.IsAssignableFrom<Control>(scroll.Content), "Major God rollover");
                    if (index == 0) IdentityPreviewAssertions.Compact(view.FindControl<TextBox>("_rolloverNameBox")!);
                }
                Assert.False(view.IsDirty, view.CreateModifiedDocumentSnapshot()?.ToString()); Assert.Equal(before, File.ReadAllBytes(path));
                if (!original)
                {
                    const string edited = "Bonus é  \nwith literal \\n  ";
                    fields.Single(field => field.Name != "_rolloverNameBox").Text = edited;
                    await Settle(window); Assert.True(view.IsDirty);
                    Assert.Equal(edited, Field<Dictionary<string, string>>(view, "_pendingStringUpdates")["STR_BONUS"]);
                }
                foreach (var field in fields)
                {
                    view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex = field.Name == "_rolloverNameBox" ? 0 : 2;
                    await Settle(window); await RolloverAssertions.Stress(window, field);
                }
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartingModesHaveSeparateCardsAndRemainClean(bool original)
    {
        using var workspace = new TestWorkspace();
        var source = Definition("Starting");
        foreach (var group in source.Elements().Where(e => e.Name.LocalName.StartsWith("starting", StringComparison.Ordinal)).ToArray())
        {
            var deathmatch = new XElement(group);
            deathmatch.SetAttributeValue("mode", "deathmatch");
            source.Add(deathmatch);
        }
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", new XElement("civsmods", source).ToString());
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView(original ? [new MajorGodDefinition("A", true, source)] : [], path,
                cultureNames: ["Greek"], protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA"]);
            var window = new Window { Content = view, Width = 1440, Height = 950 };
            try
            {
                window.Show(); view.SetModifiedMode(!original); view.SelectMajorGod("A");
                view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex = 1;
                await Settle(window);
                foreach (var name in new[] { "_startingUnitsPanel", "_startingQueuePanel", "_startingResourcesPanel" })
                {
                    var panel = Field<StackPanel>(view, name);
                    var cards = panel.Children.OfType<Border>().Where(b => b.Classes.Contains("starting-group-card")).ToArray();
                    Assert.Equal(2, cards.Length);
                    Assert.Equal(new[] { "Supremacy", "Deathmatch" }, cards.Select(card =>
                        card.GetLogicalDescendants().OfType<ComboBox>().First().SelectedItem?.ToString()));
                    foreach (var width in new[] { 950d, 680d })
                    {
                        panel.Width = width; await Settle(window);
                        foreach (var card in cards)
                        {
                            FormLayoutAssertions.NoOverlaps(card, $"{name}/{original}/{width}");
                            var shell = Assert.IsType<Grid>(card.Child);
                            var remove = shell.Children.OfType<Button>().SingleOrDefault();
                            if (original) Assert.Null(remove);
                            else
                            {
                                Assert.NotNull(remove);
                                Assert.InRange(Math.Abs(remove.Bounds.Right - shell.Bounds.Width), 0, .5);
                                Assert.InRange(remove.Bounds.Top, 0, .5);
                            }
                        }
                    }
                }
                Assert.False(view.IsDirty);
                Assert.Equal(before, File.ReadAllBytes(path));
                if (!original)
                {
                    var resources = Field<StackPanel>(view, "_startingResourcesPanel");
                    var first = resources.Children.OfType<Border>().First();
                    var add = first.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add resource"));
                    add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Settle(window);
                    var document = view.CreateModifiedDocumentSnapshot()!;
                    var groups = document.Descendants("startingresources").ToArray();
                    Assert.NotNull(groups[0].Element("gold"));
                    Assert.Null(groups[1].Element("gold"));
                    Assert.Equal(2, resources.Children.OfType<Border>().Count());
                    Assert.True(view.IsDirty);
                }
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    public void Dispose() => AvaloniaTestHost.CollectUiGarbage();
    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); await Task.Delay(30);
    }

    public static IEnumerable<object[]> MajorFamilies
        => new[] { "Properties", "Starting", "Shades", "Terrain", "TimeShifting", "BuildingChain", "CombatXP", "Bounty", "Bonus" }
            .SelectMany(family => new[] { new object[] { family, false }, new object[] { family, true } });

    private static XElement Definition(string family)
    {
        var god = XElement.Parse("<civ future='kept'><name>A</name><statsid>SP</statsid><culture>Greek</culture><key>A</key><buildingefficiency>0.6</buildingefficiency><extrabuildingefficiency>0.3</extrabuildingefficiency><secondaryprayerefficiency g='2' v='2' z='2'/></civ>");
        switch (family)
        {
            case "Starting":
                god.Add(XElement.Parse("<startingunits><unit count='1' x='-2' y='0' z='3' delay='1'>UnitA</unit></startingunits>"),
                    XElement.Parse("<startingqueue><unit count='2' delay='1'>UnitA</unit></startingqueue>"),
                    XElement.Parse("<startingresources><food>100</food><wood>20</wood></startingresources>")); break;
            case "Shades": god.Add(XElement.Parse("<shades><chance>0.2</chance><delay>10</delay><protounit>UnitA</protounit><targetunit>TypeA</targetunit><maxqueued>5</maxqueued></shades>")); break;
            case "Terrain":
                var terrain = new XElement("terraincreeps"); var creep = MajorGodCatalog.AddTerrainCreep(terrain);
                creep.SetAttributeValue("creep", "GaiaCreep"); creep.SetAttributeValue("buildability", "unbuildableenemies");
                creep.SetAttributeValue("preventplacement", "TypeA"); MajorGodCatalog.AddTerrainCreepGenerator(creep, "UnitA"); god.Add(terrain); break;
            case "TimeShifting":
                var time = new XElement("timeshifting", new XAttribute("maxconcurrenttimeshifts", "2"));
                MajorGodCatalog.AddTimeShiftingUnit(time, "UnitA"); god.Add(time); break;
            case "BuildingChain":
                var chain = new XElement("buildingchain"); MajorGodCatalog.AddBuildingChainAnchor(chain).SetAttributeValue("vfx", "UnitA");
                MajorGodCatalog.AddBuildingChainRateTier(chain); MajorGodCatalog.AddChainableBuilding(chain).Value = "UnitA"; god.Add(chain); break;
            case "CombatXP": god.Add(XElement.Parse("<combatxp><tier>10</tier><tier>20</tier></combatxp>")); break;
            case "Bounty": god.Add(XElement.Parse("<bountyresourceearning><active>true</active><bountyreward condition='Damage' unittype='TypeA' resourcetype='Favor' asspawnedunit='UnitA'>1</bountyreward><researchreward techtype='TypeTech' resourcetype='Favor'>2</researchreward></bountyresourceearning>")); break;
            case "Bonus":
                var bonus = new XElement("bonusunitspawning"); var goal = MajorGodCatalog.AddBonusUnitGoal(bonus);
                goal.SetAttributeValue("name", "GoalA"); MajorGodCatalog.AddBonusGoalChild(goal, "reward", "UnitA");
                MajorGodCatalog.AddBonusGoalChild(goal, "contributor", "1").SetAttributeValue("unit", "UnitA");
                MajorGodCatalog.AddBonusRewardPointCosts(goal); god.Add(bonus); break;
        }
        return god;
    }

    [Theory]
    [MemberData(nameof(MajorFamilies))]
    public void MajorFamiliesHaveStableRolesCleanOpeningAndWholeGroupsAtBothWidths(string family, bool original)
    {
        using var workspace = new TestWorkspace(); var source = Definition(family);
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", new XElement("civsmods", source, new XElement("civ", new XElement("name", "B"))).ToString());
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView(original ? [new MajorGodDefinition("A", true, source)] : [], path,
                cultureNames: ["Greek"], technologyNames: ["TechA"], protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA"],
                techTypeNames: ["TypeTech"], terrainCreepNames: ["GaiaCreep"]);
            var window = new Window { Content = view, Width = 1440, Height = 950 };
            try
            {
                window.Show(); view.SetModifiedMode(!original); view.SelectMajorGod("A"); await Settle(window);
                var tabs = view.FindControl<TabControl>("_majorGodTabs")!;
                tabs.SelectedIndex = family == "Properties" ? 0 : family == "Starting" ? 1 : 2;
                await Settle(window);
                var scroll = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
                var content = Assert.IsAssignableFrom<Control>(scroll.Content);
                foreach (var width in family == "Properties" ? new[] { 980d } : new[] { 980d, 680d })
                { scroll.Width = width; await Settle(window); FormLayoutAssertions.NoOverlaps(content, $"Major {family}/{original}/{width}"); }
                var inputs = content.GetLogicalDescendants().OfType<Control>().Distinct().Where(control =>
                    control.IsEffectivelyVisible && (control is AutoCompleteBox or ComboBox or AssetPathEditor || control is TextBox { AcceptsReturn: false }) &&
                    !control.GetLogicalAncestors().TakeWhile(parent => !ReferenceEquals(parent, content)).Any(parent => parent is AutoCompleteBox or ComboBox or AssetPathEditor)).ToArray();
                Assert.NotEmpty(inputs);
                foreach (var remove in content.GetLogicalDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && button.Classes.Contains("remove-card")))
                {
                    var header = Assert.IsType<Grid>(remove.Parent);
                    Assert.InRange(Math.Abs(remove.Bounds.Right - header.Bounds.Width), 0, .5);
                    Assert.InRange(remove.Bounds.Top, 0, .5);
                }
                if (family == "Properties")
                {
                    var schema = XDocument.Load(Path.Combine(ProjectSource.RootPath, "Windows", "MajorGodEditorView.axaml"));
                    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                    foreach (var node in schema.Descendants().Where(node => node.Name.LocalName is "TextBox" or "AutoCompleteBox" or "ComboBox" or "AssetPathEditor"))
                    {
                        var name = (string)node.Attribute(x + "Name")!;
                        if (name is "_keyBox" or "_rolloverNameBox") continue;
                        var input = view.FindControl<Control>(name)!;
                        Assert.NotNull(input); Assert.True(EditorFormLayout.TryGetWidthRole(input, out _), name);
                    }
                }
                foreach (var input in inputs)
                {
                    if (input.Name == "_keyBox") { Assert.Equal(70, input.Width); continue; }
                    Assert.True(EditorFormLayout.TryGetWidthRole(input, out var role), $"{family}: {input.Name}/{input.GetType().Name}");
                    Assert.Equal(EditorFormLayout.WidthFor(role), input.Width); Assert.Equal(input.Width, input.MinWidth); Assert.Equal(input.Width, input.MaxWidth);
                    Assert.True(EditorFieldInteraction.IsConfigured(input));
                    if (input.Parent is StackPanel group)
                    {
                        var index = group.Children.IndexOf(input);
                        if (index + 1 < group.Children.Count && group.Children[index + 1] is Button cross && cross.IsEffectivelyVisible && cross.Classes.Contains("remove-button"))
                        {
                            var p = input.TranslatePoint(default, window)!.Value; var q = cross.TranslatePoint(default, window)!.Value;
                            Assert.True(Math.Abs(p.Y + input.Bounds.Height / 2 - q.Y - cross.Bounds.Height / 2) <= .5,
                                $"{family}: field={input.Bounds}/{input.Margin}/{input.VerticalAlignment}, cross={cross.Bounds}/{cross.Margin}/{cross.VerticalAlignment}, parent={group.Bounds}, positions={p}/{q}");
                            FormLayoutAssertions.CrossGap(input, cross, window);
                        }
                    }
                }
                Assert.False(view.IsDirty, view.CreateModifiedDocumentSnapshot()?.ToString()); Assert.Equal(before, File.ReadAllBytes(path));
                if (!original) { view.SelectMajorGod("B"); await Settle(window); Assert.False(view.IsDirty); }
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void CompactGodAndCultureFieldsUseExplicitRolesWithoutChangingSource(bool minor, bool original)
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var source = minor ? XElement.Parse("<TechA portrait='icon.png' icon='icon.png' future='kept'/>") : XElement.Parse("<culture><name>A</name><displaynameid>STR_A</displaynameid><compendiumtopic>topic</compendiumtopic><compendiumimage>icon.png</compendiumimage><future/></culture>");
            var before = source.ToString(); var calls = 0;
            Window window = minor
                ? new MinorGodEditorWindow(new MinorGodDefinition("A", "TechA", original, source), ["TechA"], ["icon.png"], new IconPreviewService(() => null, () => null), (_, _) => { calls++; return Task.FromResult(true); })
                : new CultureEditorWindow(new CultureDefinition("A", original, source), ["background"], _ => Task.FromResult<string?>("Resolved"), (_, _, _) => { calls++; return Task.FromResult(true); });
            try
            {
                window.Show(); window.Width = window.MinWidth; await Settle(window);
                var expected = minor ? new[] { ("_techBox", 200d), ("_portraitEditor", 240d), ("_iconEditor", 240d) }
                    : new[] { ("_displayNameBox", 300d), ("_topicBox", 200d), ("_imageBox", 240d), ("_replayPanelBackgroundBox", 240d) };
                foreach (var (name, width) in expected)
                {
                    var input = Field<Control>(window, name); Assert.Equal(width, input.Width);
                    Assert.True(EditorFormLayout.TryGetWidthRole(input, out _)); Assert.True(EditorFieldInteraction.IsConfigured(input));
                    Assert.Equal(!original, input.IsEnabled);
                }
                var scroll = window.GetLogicalDescendants().OfType<ScrollViewer>().Single(control => control.Content is Grid or StackPanel);
                FormLayoutAssertions.NoOverlaps((Control)scroll.Content!, $"compact {minor}/{original}");
                Assert.Equal(before, source.ToString()); Assert.Equal(0, calls);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SharedMajorGodDataUsesRolesAndWrapsWhileRemainingReadOnly()
    {
        AvaloniaTestHost.RunAsync(async () =>
        {
            var shared = XElement.Parse("<shared><defaultpoplimit>300</defaultpoplimit><defaultrepairfactor>0.5</defaultrepairfactor><defaultcontainedregenrate>1</defaultcontainedregenrate><lightningtech>LongCustomLightningTechnology</lightningtech><deathmatchtech>TechA</deathmatchtech><storymodetech>TechB</storymodetech><ludicrousmodetech>TechC</ludicrousmodetech><deathmatchpowergrantdelay><archaicage>2</archaicage><classicalage>3</classicalage><heroicage>4</heroicage><mythicage>5</mythicage></deathmatchpowergrantdelay></shared>");
            var before = shared.ToString(); var view = new MajorGodEditorView([], null, baseSharedData: shared);
            var window = new Window { Content = view, Width = 1440, Height = 950 };
            try
            {
                window.Show(); view.SelectMajorGod(MajorGodCatalog.SharedDataDisplayName); await Settle(window);
                var tabs = view.FindControl<TabControl>("_majorGodTabs")!;
                var scroll = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
                foreach (var width in new[] { 980d, 680d })
                { scroll.Width = width; await Settle(window); FormLayoutAssertions.NoOverlaps((Control)scroll.Content!, $"shared/{width}"); }
                var fields = view.FindControl<StackPanel>("_sharedDataPropertiesPanel")!.GetLogicalDescendants().OfType<TextBox>().ToArray();
                Assert.Equal(11, fields.Length);
                Assert.All(fields, field => { Assert.False(field.IsEnabled); Assert.True(EditorFormLayout.TryGetWidthRole(field, out _)); });
                Assert.False(view.IsDirty); Assert.Equal(before, shared.ToString());
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void IncompleteShadesCanBeDiscardedButSaveStillReportsTheSpecificMissingField()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", "<civsmods><civ><name>A</name><statsid>SP</statsid></civ></civsmods>");
        var before = File.ReadAllBytes(path);
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, protoUnitNames: ["UnitA"]);
            var window = new Window { Content = view, Width = 1200, Height = 900 };
            try
            {
                window.Show(); view.SetModifiedMode(true); view.SelectMajorGod("A"); await Settle(window);
                Field<XElement>(view, "_currentEntry").Add(XElement.Parse("<shades><chance>0</chance><delay>0</delay></shades>"));
                view.FindControl<TextBox>("_nameBox")!.Text = "Renamed"; await Settle(window);
                Assert.True(await view.CaptureCurrentMajorGodDraftAsync());
                var saving = view.SaveAsync(); var messages = new List<string>();
                for (var i = 0; !saving.IsCompleted && i < 300; i++)
                {
                    foreach (var prompt in window.OwnedWindows.OfType<Prompt>().ToArray()) { messages.Add(prompt.PromptText); prompt.Close(); }
                    await Task.Delay(10);
                }
                Assert.False(await saving.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Contains(messages, message => message.Contains("Shades Unit: select a Unit", StringComparison.Ordinal));
                Assert.Equal(before, File.ReadAllBytes(path)); Assert.True(view.IsDirty);
                view.DiscardMajorGodChanges("Renamed", XElement.Parse("<civ><name>A</name><statsid>SP</statsid></civ>"));
                await Settle(window); Assert.False(view.IsDirty);
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }

    [Fact]
    public void SpecialReferenceAndNumericEditsStillSaveReopenAndPreserveUnknownContent()
    {
        using var workspace = new TestWorkspace();
        var source = Definition("BuildingChain"); source.Element("buildingchain")!.Element("anchor")!.Value = "TypeA";
        var path = workspace.WriteText("game/data/gameplay/major_gods_mods.xml", new XElement("civsmods", source, new XElement("civ", new XElement("name", "B"))).ToString());
        AvaloniaTestHost.RunAsync(async () =>
        {
            var view = new MajorGodEditorView([], path, protoUnitNames: ["UnitA"], unitTypeNames: ["TypeA", "TypeB"]);
            var window = new Window { Content = view, Width = 1200, Height = 950 };
            try
            {
                window.Show(); view.SetModifiedMode(true); view.SelectMajorGod("A");
                view.FindControl<TabControl>("_majorGodTabs")!.SelectedIndex = 2; await Settle(window);
                Assert.False(view.IsDirty);
                var panel = view.FindControl<StackPanel>("_specialMechanicsPanel")!;
                var anchor = panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.Text == "TypeA");
                anchor.SelectedItem = "TypeB";
                var rate = panel.GetLogicalDescendants().OfType<StackPanel>().Distinct()
                    .Single(group => group.Children.OfType<TextBlock>().Any(label => label.Text == "Rate tile/minute"))
                    .Children.OfType<TextBox>().Single();
                rate.Text = "2.5"; await Settle(window); Assert.True(view.IsDirty);
                var saving = view.SaveAsync(); var errors = new List<string>();
                for (var i = 0; !saving.IsCompleted && i < 500; i++)
                {
                    foreach (var prompt in window.OwnedWindows.OfType<Prompt>().ToArray()) { errors.Add(prompt.PromptText); prompt.Close(); }
                    await Task.Delay(10);
                }
                Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(3)), string.Join("\n", errors)); Assert.Empty(errors);
                var saved = XDocument.Load(path).Descendants("civ").Single(god => god.Element("name")!.Value == "A");
                Assert.Equal("TypeB", saved.Element("buildingchain")!.Element("anchor")!.Value);
                Assert.Equal("2.5", saved.Descendants("tilerateperminutetier").Single().Value);
                Assert.Equal("kept", (string?)saved.Attribute("future"));
                view.SelectMajorGod("B"); view.SelectMajorGod("A"); await Settle(window); Assert.False(view.IsDirty);
            }
            finally { window.Close(); view.ReleaseMajorGodXmlEditing(); }
        });
    }
}
