using System.Collections;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static Control StatsModule(BaselineHost host, string key)
        => (Control)Field<IDictionary>(host, "_otherSpecificAttributeContainers")[key]!;
    private static AutoCompleteBox RechargeFilter(Control module, string title)
        => Assert.Single(module.GetLogicalDescendants().OfType<AutoCompleteBox>(), box =>
            box.Parent is StackPanel header && header.Children.OfType<TextBlock>().Any(text => text.Text == title));

    [Fact]
    public void UnitContainBonusStatsOpensWithoutDirtyingAndKeepsItsExistingFieldLifecycle()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var panel = Field<StackPanel>(host, "_statsEditorPanel");
            var opener = Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Bonus stats"));
            Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "HP"));
            opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            foreach (var label in new[] { "HP", "Speed", "Regen rate" })
                Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, label));
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "HP"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var field = (TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["containedhitpointbonus"];
            field.Text = "2"; await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Equal("2", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("containedhitpointbonus")!.Value);
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Bonus stats"));
            field = (TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["containedhitpointbonus"];
            Assert.Equal("2", field.Text); Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Single(Assert.IsType<StackPanel>(field.Parent).Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Null(unit.Element("containedhitpointbonus"));
            Assert.Equal("2", unit.Element("maxcontained")!.Value);
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
        }, RoleDocument("<maxcontained>2</maxcontained><contain>CheckpointType</contain>"), RoleFixture);

    [Theory]
    [InlineData("recharge", "Add Recharge Include/Exclude Types")]
    [InlineData("auxrecharge", "Add Aux Recharge Include/Exclude Types")]
    public void UnitRechargeFiltersShareTheLabelColumnAndPreserveDataThroughTypeSwitches(string key, string openerText)
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1500; await RoleSettle(host);
            var module = StatsModule(host, key);
            var mode = (ComboBox)Field<Dictionary<string, Control>>(host, "_fieldControls")[key + ".mode"];
            var opener = Assert.Single(module.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, openerText));
            var typeLabel = Assert.Single(Assert.IsType<StackPanel>(mode.Parent).Children.OfType<TextBlock>());
            var expectedLeft = typeLabel.TranslatePoint(default, host)!.Value.X;
            Assert.InRange(Math.Abs(opener.TranslatePoint(default, host)!.Value.X - expectedLeft), 0, .5);
            Assert.False(Field<bool>(host, "_isDirty"));
            opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var include = RechargeFilter(module, "Include Type");
            var exclude = RechargeFilter(module, "Exclude Type");
            Assert.Equal(50, include.Bounds.Width); Assert.Equal(50, exclude.Bounds.Width);
            Assert.Null(include.PlaceholderText); Assert.Null(exclude.PlaceholderText);
            var label = Assert.Single(Assert.IsType<StackPanel>(include.Parent).Children.OfType<TextBlock>());
            Assert.InRange(Math.Abs(label.TranslatePoint(default, host)!.Value.X - expectedLeft), 0, .5);
            include.SelectedItem = RoleUnit; await RoleSettle(host);
            exclude.SelectedItem = "CheckpointType"; await RoleSettle(host);
            mode.SelectedItem = "ResourceDropoff"; await RoleSettle(host);
            Assert.DoesNotContain(module.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, openerText));
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal(RoleUnit, unit.Element(key + "includetypes")!.Element("unittype")!.Value);
            Assert.Equal("CheckpointType", unit.Element(key + "excludetypes")!.Element("unittype")!.Value);
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            module = StatsModule(host, key);
            mode = (ComboBox)Field<Dictionary<string, Control>>(host, "_fieldControls")[key + ".mode"];
            Assert.False(Field<bool>(host, "_isDirty"));
            mode.SelectedItem = "Kills"; await RoleSettle(host);
            include = RechargeFilter(module, "Include Type");
            var filtersGrid = include.GetLogicalAncestors().OfType<Grid>().First();
            Assert.Single(filtersGrid.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RoleSettle(host); Assert.Empty(await Save(host));
            unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Null(unit.Element(key + "includetypes")); Assert.Null(unit.Element(key + "excludetypes"));
            Assert.Equal("10", unit.Element(key)!.Value);
        }, RoleDocument($"<{key} type='Kills' init='1'>10</{key}>"), RoleFixture);

    [Theory]
    [InlineData("recharge")]
    [InlineData("auxrecharge")]
    public void UnitResourceDropoffDoesNotOfferATypeFilterOpener(string key)
        => WithHost(async (_, host) =>
        {
            await RoleSettle(host);
            var module = StatsModule(host, key);
            Assert.DoesNotContain(module.GetLogicalDescendants().OfType<Button>(), button =>
                button.Content is string text && text.Contains("Include/Exclude", StringComparison.Ordinal));
            Assert.False(Field<bool>(host, "_isDirty"));
            var mode = (ComboBox)Field<Dictionary<string, Control>>(host, "_fieldControls")[key + ".mode"];
            mode.SelectedItem = "Kills"; await RoleSettle(host);
            Assert.Single(module.GetLogicalDescendants().OfType<Button>(), button =>
                button.Content is string text && text.Contains("Include/Exclude", StringComparison.Ordinal));
            mode.SelectedItem = "ResourceDropoff"; await RoleSettle(host);
            Assert.DoesNotContain(module.GetLogicalDescendants().OfType<Button>(), button =>
                button.Content is string text && text.Contains("Include/Exclude", StringComparison.Ordinal));
        }, RoleDocument($"<{key} type='resourceDropoff' init='1'>10</{key}>"), RoleFixture);
}
