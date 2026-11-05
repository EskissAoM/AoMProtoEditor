using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    private static string StatsEnumDocument(string modifyType = "DamageSpecific", string damageType = "Hack")
        => RoleDocument($"""
            <veterancyranks><rank><numkills>2</numkills></rank></veterancyranks>
            <veterancybonus><rank id="0"><veterancymodify modifytype="{modifyType}" damagetype="{damageType}">1.2</veterancymodify></rank>
                <includetypes><unittype>Unit</unittype></includetypes></veterancybonus>
            <OnDamageModifiers><OnDamageModify modifyType="{modifyType}" damageType="{damageType}">2</OnDamageModify></OnDamageModifiers>
            """);

    [Theory]
    [InlineData("_veterancyBonusRows", "DamageTypeAcb", "veterancybonus", "veterancymodify", "damagetype")]
    [InlineData("_onDamageModifyRows", "DamageTypeCb", "OnDamageModifiers", "OnDamageModify", "damageType")]
    public void UnitStatsModifiersUseSharedEnumsAndPreserveConditionalSelectionOnSave(
        string rowsField, string damageProperty, string moduleTag, string elementTag, string attribute)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var row = Assert.Single(Field<IList>(host, rowsField).Cast<object>());
            var modify = Assignment<StrictSearchableComboBox>(row, "ModifyTypeAcb");
            var damage = Assignment<StrictSearchableComboBox>(row, damageProperty);
            AssertRole(modify, EditorFieldWidthRole.LongEnum); AssertRole(damage, EditorFieldWidthRole.ShortEnum);
            Assert.Equal(ProtoConstants.KnownDamageTypes.Order(), damage.Options.Order());
            Assert.False(Field<bool>(host, "_isDirty"));
            modify.SelectedItem = "Damage"; await RoleSettle(host);
            Assert.False(damage.IsEffectivelyVisible); Assert.Equal("", damage.Text);
            modify.SelectedItem = "ArmorSpecific"; await RoleSettle(host);
            Assert.True(damage.IsEffectivelyVisible);
            // The real shared editable ComboBox opens the complete closed catalog.
            damage.BringIntoView(); await RoleSettle(host);
            RoleClick(host, damage.GetVisualDescendants().OfType<TextBox>().First()); await RoleSettle(host);
            Assert.True(damage.IsDropDownOpen);
            Assert.Equal(ProtoConstants.KnownArmorTypes.Order(), damage.ItemsSource!.Cast<string>().Order());
            damage.SelectedItem = "Pierce"; damage.IsDropDownOpen = false; await RoleSettle(host);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            var saved = unit.Element(moduleTag)!.Descendants(elementTag).Single();
            Assert.Equal("Pierce", (string?)saved.Attribute(attribute));
            Assert.Equal("yes", (string?)unit.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            row = Assert.Single(Field<IList>(host, rowsField).Cast<object>());
            Assert.Equal("Pierce", Assignment<StrictSearchableComboBox>(row, damageProperty).Text);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, StatsEnumDocument(), RoleFixture);

    [Theory]
    [InlineData("DamageSpecific", "LegacyDamage")]
    [InlineData("FutureModifyType", "LegacyDamage")]
    public void UnitStatsEnumMigrationKeepsExistingUnknownValues(string modifyType, string damageType)
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host); Assert.False(Field<bool>(host, "_isDirty"));
            var row = Assert.Single(Field<IList>(host, "_veterancyBonusRows").Cast<object>());
            Assignment<TextBox>(row, "ValueTb").Text = "1.5";
            await RoleSettle(host); Assert.True(Field<bool>(host, "_isDirty"));
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal("1.5", unit.Element("veterancybonus")!.Descendants("veterancymodify").Single().Value);
            foreach (var (element, type, damage) in new[]
            {
                (unit.Element("veterancybonus")!.Descendants("veterancymodify").Single(), "modifytype", "damagetype"),
                (unit.Element("OnDamageModifiers")!.Element("OnDamageModify")!, "modifyType", "damageType")
            })
            {
                Assert.Equal(modifyType, (string?)element.Attribute(type));
                Assert.Equal(damageType, (string?)element.Attribute(damage));
            }
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, StatsEnumDocument(modifyType, damageType), RoleFixture);
}
