using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void UnitR5R6OriginalModulesStayReadOnlyWithoutExtraRowsOrDirtyState()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Set("_barXmlRoot", XElement.Parse(RoleDocument(
                $"<spawn type='birth' count='1'>{RoleUnit}</spawn><resourceconversion from='Food' to='Gold'>2</resourceconversion>" +
                $"<replacement type='birth' lifespan='6'>{RoleUnit}</replacement><minimapicon>custom_minimap</minimapicon><minimapsize>2</minimapsize><minimapcolor red='128'/>")));
            host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.True(Field<bool>(host, "_isReadOnly"));
            Assert.Single(Field<IList>(host, "_spawnRows").Cast<object>());
            Assert.Single(Field<IList>(host, "_resourceConversionRows").Cast<object>());
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            foreach (var key in new[] { "replacement.type", "replacement.value", "replacement.lifespan", "minimapicon", "minimapsize", "minimapcolor.red" })
                Assert.False(fields[key].IsEnabled);
            foreach (var key in new[] { "spawndata", "resourceconversion", "replacement", "minimapvisuals" })
                Assert.DoesNotContain(StatsModule(host, key).GetLogicalDescendants().OfType<Button>(), button =>
                    button.Classes.Contains("add-component") || button.Classes.Contains("remove-button"));
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData("Spawn", "Add Spawn", "_spawnRows", "spawn")]
    [InlineData("Resource Conversion", "Add Conversion", "_resourceConversionRows", "resourceconversion")]
    public void UnitR5ModulesOpenTheirFirstRowAndRepeatedAddRemoveSaveWorks(string label, string addText, string field, string tag)
        => WithHost(async (fixture, host) =>
        {
            await AddUnitOptional(host, label);
            var rows = Field<IList>(host, field); Assert.Single(rows.Cast<object>());
            var module = StatsModule(host, tag == "spawn" ? "spawndata" : tag);
            var add = Assert.Single(module.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, addText));
            for (var i = 0; i < 2; i++) { add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host); }
            Assert.Equal(3, rows.Count);
            var last = Assignment<Panel>(rows[^1]!, "RowPanel");
            Assert.Single(last.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-button"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Equal(2, rows.Count); Assert.Null(last.Parent);
            foreach (var row in rows.Cast<object>())
            {
                if (tag == "spawn")
                {
                    var type = Assignment<ComboBox>(row, "TypeCb");
                    Assert.Contains("Birth", type.ItemsSource!.Cast<string>()); type.SelectedItem = "Birth";
                    Assignment<AutoCompleteBox>(row, "ValueAcb").SelectedItem = RoleUnit;
                }
                else
                {
                    Assignment<ComboBox>(row, "FromResourceCb").SelectedItem = "Food";
                    Assignment<ComboBox>(row, "ToResourceCb").SelectedItem = "Gold";
                    Assignment<TextBox>(row, "ValueTb").Text = "2.5";
                }
            }
            await RoleSettle(host); Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal(2, unit.Elements(tag).Count());
            if (tag == "spawn") Assert.All(unit.Elements(tag), element => Assert.Equal("birth", (string?)element.Attribute("type")));
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Equal(2, Field<IList>(host, field).Count); Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData("birth", "Birth")]
    [InlineData("hitGround", "HitGround")]
    [InlineData("futureMode", "futureMode")]
    public void UnitR5TriggerCaptionsRoundTripWithoutChangingXmlTokens(string token, string caption)
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var spawn = Assert.Single(Field<IList>(host, "_spawnRows").Cast<object>());
            Assert.Equal(caption, Assignment<ComboBox>(spawn, "TypeCb").SelectedItem);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var type = Assert.IsType<StrictSearchableComboBox>(fields["replacement.type"]);
            Assert.Equal(caption, type.Text); Assert.False(Field<bool>(host, "_isDirty"));
            ((TextBox)fields["maxhitpoints"]).Text = "12"; await RoleSettle(host); Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal(token, (string?)unit.Element("spawn")!.Attribute("type"));
            Assert.Equal(token, (string?)unit.Element("replacement")!.Attribute("type"));
            Assert.Equal("yes", (string?)unit.Element("replacement")!.Attribute("future"));
        }, RoleDocument($"<spawn type='{token}' count='1'>{RoleUnit}</spawn><replacement type='{token}' future='yes'>{RoleUnit}</replacement>"), RoleFixture);

    [Fact]
    public void UnitR5ReplacementLifespanFlowsBesideTheValueAndKeepsItsFieldCrossAndSaveBehavior()
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var module = StatsModule(host, "replacement");
            var add = Assert.Single(module.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Add lifespan"));
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var value = fields["replacement.value"];
            Assert.InRange(Math.Abs(add.TranslatePoint(default, host)!.Value.Y - value.TranslatePoint(default, host)!.Value.Y), 0, .5);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var lifespan = Assert.IsType<TextBox>(fields["replacement.lifespan"]); AssertRole(lifespan, EditorFieldWidthRole.Numeric);
            var cross = Assert.Single(Assert.IsType<StackPanel>(lifespan.Parent).Children.OfType<Button>());
            var point = lifespan.TranslatePoint(default, host)!.Value; var removePoint = cross.TranslatePoint(default, host)!.Value;
            Assert.InRange(removePoint.X - point.X - lifespan.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(point.Y - value.TranslatePoint(default, host)!.Value.Y), 0, .5);
            lifespan.Text = "6.5"; await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Equal("6.5", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("replacement")!.Attribute("lifespan"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            lifespan = (TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["replacement.lifespan"];
            Assert.Single(Assert.IsType<StackPanel>(lifespan.Parent).Children.OfType<Button>())
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Null(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("replacement")!.Attribute("lifespan"));
        }, RoleDocument($"<replacement type='birth'>{RoleUnit}</replacement>"), RoleFixture);
}
