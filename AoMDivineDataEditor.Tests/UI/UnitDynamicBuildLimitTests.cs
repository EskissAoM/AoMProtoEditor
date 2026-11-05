using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static string DynamicLimitDocument(string targets = "<unittype>CheckpointType</unittype>")
        => RoleDocument($"<buildlimit>3</buildlimit><dynamicbuildlimitunittypes>{targets}</dynamicbuildlimitunittypes>");

    private static AutoCompleteBox DynamicLimitPicker(BaselineHost host)
        => Assert.IsType<AutoCompleteBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["dynamicbuildlimitunittypes"]);

    private static StackPanel StatsChipGroup(AutoCompleteBox picker)
        => picker.GetLogicalAncestors().OfType<StackPanel>().First(panel => panel.Children.OfType<WrapPanel>().Any());

    private static void RemoveStatsChip(Control group, string value)
    {
        var text = group.GetLogicalDescendants().OfType<TextBlock>().First(text => text.Text == value &&
            text.Parent is StackPanel row && row.Children.OfType<Button>().Any(button => button.Classes.Contains("chip-remove-button")));
        Assert.Single(Assert.IsType<StackPanel>(text.Parent).Children.OfType<Button>())
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public void DynamicBuildLimitUsesCompactSharedCatalogChipsAndSavesOnlyChosenValues()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = XDocument.Load(fixture.ProtoPath);
            var picker = DynamicLimitPicker(host);
            AssertRole(picker, EditorFieldWidthRole.ChipSearch);
            var group = StatsChipGroup(picker);
            Assert.Contains(group.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Unit");
            Assert.Empty(Field<IList>(host, "_buildLimitRows"));
            Assert.DoesNotContain(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<Button>(),
                button => Equals(button.Content, "Add Dynamic Type"));
            Assert.Contains(RoleUnit, picker.ItemsSource!.Cast<string>());
            Assert.DoesNotContain("CheckpointType", picker.ItemsSource!.Cast<string>());
            Assert.False(Field<bool>(host, "_isDirty"));
            picker.SelectedItem = RoleUnit; await RoleSettle(host);
            Assert.DoesNotContain(RoleUnit, picker.ItemsSource!.Cast<string>());
            Assert.Equal("", picker.Text);
            RemoveStatsChip(group, "CheckpointType"); await RoleSettle(host);
            Assert.Contains("CheckpointType", picker.ItemsSource!.Cast<string>());
            picker.Text = "NotACatalogValue"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            var unit = saved.Root!.Element("unit")!;
            Assert.Equal(RoleUnit, Assert.Single(unit.Element("dynamicbuildlimitunittypes")!.Elements("unittype")).Value);
            Assert.Null(unit.Element("sharedbuildlimitunittypes"));
            Assert.Equal("3", unit.Element("buildlimit")!.Value);
            Assert.True(XNode.DeepEquals(before.Root!.Element("unit")!.Element("future"), unit.Element("future")));
            Assert.True(XNode.DeepEquals(before.Root.Elements("unit").Last(), saved.Root.Elements("unit").Last()));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal(new[] { RoleUnit }, Field<List<string>>(host, "_dynamicBuildLimitTypes"));
            Assert.False(Field<bool>(host, "_isDirty"));
        }, DynamicLimitDocument(), RoleFixture);

    [Fact]
    public void DynamicBuildLimitSwitchesThroughSharedAndStandardWithoutStaleTargets()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var mode = Assert.IsType<ComboBox>(fields["buildlimit.mode"]);
            mode.SelectedItem = "Shared Build Limit"; await RoleSettle(host);
            var row = Assert.Single(Field<IList>(host, "_buildLimitRows").Cast<object>());
            Assignment<TextBox>(row, "WeightTb").Text = "2";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.Equal("2", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!
                .Element("sharedbuildlimitunittypes")!.Element("unittype")!.Attribute("weight"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            mode = (ComboBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["buildlimit.mode"];
            mode.SelectedItem = "Dynamic"; await RoleSettle(host);
            DynamicLimitPicker(host).SelectedItem = RoleUnit; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal(2, unit.Element("dynamicbuildlimitunittypes")!.Elements("unittype").Count());
            Assert.Null(unit.Element("sharedbuildlimitunittypes"));
            Assert.DoesNotContain(unit.Elements("flag"), flag => flag.Value == "UseSharedBuildLimit");
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            mode = (ComboBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["buildlimit.mode"];
            mode.SelectedItem = "Standard"; await RoleSettle(host);
            Assert.Empty(Field<List<string>>(host, "_dynamicBuildLimitTypes"));
            Assert.Empty(await Save(host));
            Assert.Null(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("dynamicbuildlimitunittypes"));
        }, DynamicLimitDocument(), RoleFixture);

    [Fact]
    public void DynamicBuildLimitRetainsDuplicateProtectionAndCanRemoveOneDuplicateChip()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var bytes = File.ReadAllBytes(fixture.ProtoPath);
            Assert.Equal(2, Field<List<string>>(host, "_dynamicBuildLimitTypes").Count);
            ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["buildlimit"]).Text = "4";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Contains(await Save(host), error => error.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(bytes, File.ReadAllBytes(fixture.ProtoPath));
            var group = StatsChipGroup(DynamicLimitPicker(host));
            RemoveStatsChip(group, "Unit"); await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Single(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!
                .Element("dynamicbuildlimitunittypes")!.Elements("unittype"));
        }, DynamicLimitDocument("<unittype>Unit</unittype><unittype>unit</unittype>"), RoleFixture);

    [Fact]
    public void DynamicBuildLimitDiscardAndLegacyValuesStayPreserved()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var original = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            var kind = Enum.Parse(typeof(ProtoEditorWindow).GetNestedType("EditorEntityKind", BindingFlags.NonPublic)!, "Units");
            var tab = host.Call("CreateDocumentTab", true, kind, true, "A")!;
            tab.GetType().GetProperty("SavedElement")!.SetValue(tab, new XElement(original));
            DynamicLimitPicker(host).SelectedItem = RoleUnit; await RoleSettle(host);
            host.Call("DiscardUnitDocumentChanges", tab);
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal(new[] { "LegacyUnitType" }, Field<List<string>>(host, "_dynamicBuildLimitTypes"));
            ((TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["buildlimit"]).Text = "4";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.Equal("LegacyUnitType", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!
                .Element("dynamicbuildlimitunittypes")!.Element("unittype")!.Value);
        }, DynamicLimitDocument("<unittype>LegacyUnitType</unittype>"), RoleFixture);

    [Fact]
    public void DynamicBuildLimitOriginalIsReadOnlyAndContainUsesTheSamePickerBehavior()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var panel = Field<StackPanel>(host, "_statsEditorPanel");
            var picker = Assert.Single(panel.GetLogicalDescendants().OfType<AutoCompleteBox>(), box => box.Width == 50 &&
                box.Parent is StackPanel header && header.Children.OfType<TextBlock>().Any(text => text.Text == "Contain"));
            var group = StatsChipGroup(picker);
            picker.SelectedItem = RoleUnit; await RoleSettle(host);
            Assert.DoesNotContain(RoleUnit, picker.ItemsSource!.Cast<string>());
            RemoveStatsChip(group, "Unit"); await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Equal(RoleUnit, Assert.Single(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements("contain")).Value);
            var bytes = File.ReadAllBytes(fixture.ProtoPath);
            host.Set("_barXmlRoot", XElement.Parse(DynamicLimitDocument()));
            host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.False(Field<Dictionary<string, Control>>(host, "_fieldControls").ContainsKey("dynamicbuildlimitunittypes"));
            Assert.Equal(new[] { "CheckpointType" }, Field<List<string>>(host, "_dynamicBuildLimitTypes"));
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(bytes, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument("<maxcontained>2</maxcontained><contain>Unit</contain>"), RoleFixture);

    [Fact]
    public void DynamicBuildLimitChipsStartBesideThePickerWhileContainKeepsItsSecondRow()
        => WithHost(async (_, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var picker = DynamicLimitPicker(host);
            var group = StatsChipGroup(picker);
            var chipText = Assert.Single(group.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "CheckpointType");
            var chip = Assert.IsType<Border>(chipText.Parent!.Parent);
            var inputPoint = picker.TranslatePoint(default, host)!.Value;
            var chipPoint = chip.TranslatePoint(default, host)!.Value;
            Assert.InRange(chipPoint.X - inputPoint.X - picker.Bounds.Width, 7.9, 12.1);
            Assert.InRange(Math.Abs(chipPoint.Y + chip.Bounds.Height / 2 - inputPoint.Y - picker.Bounds.Height / 2), 0, .5);
            var containPicker = Assert.Single(Field<StackPanel>(host, "_statsEditorPanel").GetLogicalDescendants().OfType<AutoCompleteBox>(), box =>
                box.Parent is StackPanel header && header.Children.OfType<TextBlock>().Any(text => text.Text == "Contain"));
            var contain = StatsChipGroup(containPicker);
            var containChipText = Assert.Single(contain.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Unit");
            Assert.True(containChipText.TranslatePoint(default, host)!.Value.Y > containPicker.TranslatePoint(default, host)!.Value.Y + containPicker.Bounds.Height);
            host.Width = 1000; await RoleSettle(host);
            Assert.Equal(50, picker.Bounds.Width);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<buildlimit>3</buildlimit><dynamicbuildlimitunittypes><unittype>CheckpointType</unittype></dynamicbuildlimitunittypes><maxcontained>2</maxcontained><contain>Unit</contain>"), RoleFixture);
}
