using System.Collections;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static Button DependentButton(Control container, string text)
        => Assert.Single(container.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, text));

    private static Button DependentRemove(object row)
        => Assert.Single(Assignment<Panel>(row, "RowPanel").GetLogicalDescendants().OfType<Button>(),
            button => button.Classes.Contains("remove-row"));

    [Fact]
    public void DependentOriginalRecordKeepsItsReadOnlyBoneWithoutAddingRowsOrControls()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            host.Set("_barXmlRoot", XElement.Parse(RoleDocument($"<dependentunit x='1' z='0' attachbone='LegacyBone'>{RoleUnit}</dependentunit>")));
            host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            var row = Assert.Single(Field<IList>(host, "_dependentUnitRows").Cast<object>());
            var panel = Assignment<Panel>(row, "RowPanel");
            var bone = Assignment<AutoCompleteBox>(row, "AttachBoneTb");
            Assert.Equal("LegacyBone", bone.Text); Assert.False(bone.IsEnabled);
            Assert.Empty(panel.GetLogicalDescendants().OfType<Button>());
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void DependentUnitStartsWithOneRowAndLastRemovalClosesAndRestoresTheModule()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            await AddUnitOptional(host, "Dependent Unit");
            var rows = Field<IList>(host, "_dependentUnitRows");
            var row = Assert.Single(rows.Cast<object>());
            var containers = Field<IDictionary>(host, "_otherSpecificAttributeContainers");
            var module = Assert.IsAssignableFrom<Control>(containers["dependentunitdata"]);
            var bone = Assignment<AutoCompleteBox>(row, "AttachBoneTb");
            Assert.False(bone.IsEffectivelyVisible);
            Assert.True(DependentButton(module, "Attach Bone").IsEffectivelyVisible);
            // Even an invalid detached reference must not affect a subsequent save.
            Assignment<AutoCompleteBox>(row, "ValueAcb").Text = "UnknownRemovedDependent";
            DependentRemove(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Empty(rows); Assert.False(containers.Contains("dependentunitdata")); Assert.Null(module.Parent);
            await AddUnitOptional(host, "Dependent Unit");
            row = Assert.Single(rows.Cast<object>());
            Assignment<AutoCompleteBox>(row, "ValueAcb").SelectedItem = RoleUnit;
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal(RoleUnit, Assert.Single(unit.Elements("dependentunit")).Value);
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            Assert.NotEqual(before, File.ReadAllBytes(fixture.ProtoPath));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = Assert.Single(Field<IList>(host, "_dependentUnitRows").Cast<object>());
            DependentRemove(row).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Empty(XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Elements("dependentunit"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.Empty(Field<IList>(host, "_dependentUnitRows")); Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument(), RoleFixture);

    [Fact]
    public void DependentAttachBoneIsOptionalAndExistingUnknownBoneSurvivesSaveAndReopen()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var row = Assert.Single(Field<IList>(host, "_dependentUnitRows").Cast<object>());
            var panel = Assignment<Panel>(row, "RowPanel");
            var bone = Assignment<AutoCompleteBox>(row, "AttachBoneTb");
            Assert.True(bone.IsEffectivelyVisible); Assert.Equal("LegacyBone", bone.Text);
            Assert.False(Field<bool>(host, "_isDirty"));
            Assignment<TextBox>(row, "XTb").Text = "2";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            Assert.Equal("LegacyBone", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("dependentunit")!.Attribute("attachbone"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = Assert.Single(Field<IList>(host, "_dependentUnitRows").Cast<object>());
            panel = Assignment<Panel>(row, "RowPanel"); bone = Assignment<AutoCompleteBox>(row, "AttachBoneTb");
            Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-field"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.False(bone.IsEffectivelyVisible); Assert.Equal("", bone.Text);
            DependentButton(panel, "Attach Bone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.True(bone.IsEffectivelyVisible); bone.Text = "ReplacementBone";
            Assert.Empty(await Save(host));
            Assert.Equal("ReplacementBone", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("dependentunit")!.Attribute("attachbone"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = Assert.Single(Field<IList>(host, "_dependentUnitRows").Cast<object>());
            panel = Assignment<Panel>(row, "RowPanel");
            Assert.Single(panel.GetLogicalDescendants().OfType<Button>(), button => button.Classes.Contains("remove-field"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            var dependent = Assert.Single(unit.Elements("dependentunit"));
            Assert.Null(dependent.Attribute("attachbone")); Assert.Equal("2", (string?)dependent.Attribute("x"));
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
        }, RoleDocument($"<dependentunit x='1' z='-1' attachbone='LegacyBone'>{RoleUnit}</dependentunit>"), RoleFixture);
}
