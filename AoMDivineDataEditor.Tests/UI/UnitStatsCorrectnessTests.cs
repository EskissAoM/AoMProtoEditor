using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.GameData;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Theory]
    [InlineData("_veterancyBonusRows", "DamageTypeAcb", "veterancybonus", "veterancymodify", "damagetype")]
    [InlineData("_onDamageModifyRows", "DamageTypeCb", "OnDamageModifiers", "OnDamageModify", "damageType")]
    public void UnitArmorSpecificClearsDivineAndRejectsReentryAtSave(
        string rowsField, string damageProperty, string moduleTag, string elementTag, string attribute)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var row = Assert.Single(Field<IList>(host, rowsField).Cast<object>());
            var modify = Assignment<StrictSearchableComboBox>(row, "ModifyTypeAcb");
            var damage = Assignment<StrictSearchableComboBox>(row, damageProperty);
            Assert.Equal("Divine", damage.Text);
            modify.SelectedItem = "ArmorSpecific"; await RoleSettle(host);
            Assert.Equal(ProtoConstants.KnownArmorTypes.Order(), damage.Options.Order());
            Assert.Equal("", damage.Text); Assert.Equal("", damage.Value);
            damage.Text = "Divine"; await RoleSettle(host);
            Assert.Contains(await Save(host), error => error.Contains("Damage Type", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            damage.IsDropDownOpen = false; await RoleSettle(host);
            damage.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent)); await RoleSettle(host);
            Assert.Equal("", damage.Value); Assert.Equal("", damage.Text);
            damage.IsDropDownOpen = true; damage.SelectedItem = "Hack";
            damage.IsDropDownOpen = false; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal("Hack", (string?)unit.Element(moduleTag)!.Descendants(elementTag).Single().Attribute(attribute));
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = Assert.Single(Field<IList>(host, rowsField).Cast<object>());
            modify = Assignment<StrictSearchableComboBox>(row, "ModifyTypeAcb");
            damage = Assignment<StrictSearchableComboBox>(row, damageProperty);
            modify.SelectedItem = "DamageSpecific"; await RoleSettle(host);
            Assert.Contains("Divine", damage.Options);
            damage.IsDropDownOpen = true; damage.SelectedItem = "Divine";
            damage.IsDropDownOpen = false; await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Equal("Divine", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!
                .Element(moduleTag)!.Descendants(elementTag).Single().Attribute(attribute));
        }, StatsEnumDocument("DamageSpecific", "Divine"), RoleFixture);

    [Fact]
    public void UnitLegacyArmorDivineStaysPreservedButIsNotOffered()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); Assert.False(Field<bool>(host, "_isDirty"));
            var row = Assert.Single(Field<IList>(host, "_onDamageModifyRows").Cast<object>());
            var damage = Assignment<StrictSearchableComboBox>(row, "DamageTypeCb");
            Assert.DoesNotContain("Divine", damage.Options); Assert.Equal("Divine", damage.Text);
            Assignment<TextBox>(row, "ValueTb").Text = "3"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Equal("Divine", (string?)XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!
                .Element("OnDamageModifiers")!.Element("OnDamageModify")!.Attribute("damageType"));
        }, StatsEnumDocument("ArmorSpecific", "Divine"), RoleFixture);

    [Fact]
    public void UnitOnDiscoverLosIsNumericAndCanBeSavedReopenedOrCleared()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            var editor = Assert.IsType<TextBox>(fields["ondiscoverlos"]);
            AssertRole(editor, EditorFieldWidthRole.Numeric); Assert.Equal("2.5", editor.Text);
            Assert.False(Field<bool>(host, "_isDirty"));
            editor.Text = "7.25"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            Assert.Equal("7.25", XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("ondiscoverlos")!.Value);
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            editor = (TextBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["ondiscoverlos"];
            Assert.Equal("7.25", editor.Text); Assert.False(Field<bool>(host, "_isDirty"));
            editor.Text = ""; await RoleSettle(host); Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Null(unit.Element("ondiscoverlos")); Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
        }, RoleDocument("<ondiscoverlos>2.5</ondiscoverlos>"), RoleFixture);

    [Fact]
    public void UnitPlacementFileCatalogUsesArchiveAndModFoldersAndPreservesLegacyPaths()
        => WithHost(async (fixture, host) =>
        {
            using var metadata = BarArchiveTests.BuildArchive(new BarArchiveTests.TestEntry(
                "gameplay/placement_rules/archive.xml.XMB", [], 0, false));
            var archive = new BarArchive(metadata); Assert.True(archive.Load(out _));
            host.Set("_protoDataBarFile", archive); host.Call("InvalidateBarDerivedCaches");
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            var catalog = Assert.IsType<List<string>>(host.Call("GetKnownPlacementFileNames"));
            Assert.Equal(new[] { "archive.xml", "custom.xml" }, catalog);
            var editor = Assert.IsType<AutoCompleteBox>(Field<Dictionary<string, Control>>(host, "_fieldControls")["placementfile"]);
            Assert.Equal("legacy.xml", editor.Text); Assert.False(Field<bool>(host, "_isDirty"));
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/placement_rules/new.xml", "<anything/>");
            Assert.Contains("new.xml", Assert.IsType<List<string>>(host.Call("GetKnownPlacementFileNames")));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            editor = (AutoCompleteBox)Field<Dictionary<string, Control>>(host, "_fieldControls")["placementfile"];
            Assert.Contains("new.xml", editor.ItemsSource!.Cast<string>());
            editor.SelectedItem = "new.xml"; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal("new.xml", unit.Element("placementfile")!.Value);
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<placementfile>legacy.xml</placementfile>"), fixture =>
        {
            RoleFixture(fixture);
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/placement_rules/custom.xml", "<anything/>");
            fixture.Workspace.WriteText("TestMod/game/data/gameplay/wrong.xml", "<anything/>");
        });
}
