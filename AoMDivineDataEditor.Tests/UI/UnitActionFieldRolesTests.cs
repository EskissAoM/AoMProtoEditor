using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LureVisualLabelsStayWithMarkerWrappedFieldsInOriginalAndCustomActions(bool original)
        => WithHost(async (fixture, host) =>
        {
            if (original)
            {
                host.Set("_barXmlRoot", XElement.Parse(RoleDocument("<protoaction><name>LureAction</name><type>Lure</type><anim>DashStart</anim><reloadanim>DashEnd</reloadanim><impacteffect>effects\\impacts\\pierce</impacteffect></protoaction>")));
                host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
                host.Call("RefreshProtoActionMetadata", (object)null!);
                // This minimal host does not load the archive's action-type
                // inventory. Native startup supplies Lure from that inventory.
                host.Set("_protoActionTypeSuggestions", new List<string> { "Lure" });
                host.Call("BuildEditorPanel", "A", true);
            }
            else PrepareSnippetActions(host);
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 1;
            await RoleSettle(host);
            var action = RemovalAction(host);
            Assert.Equal("Lure", Assignment<AutoCompleteBox>(action, "TypeAcb").Text);
            var body = Assignment<StackPanel>(action, "Container");
            var labels = body.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
            Assert.Contains(labels, text => text.Text == "Impact Effect:");
            var impact = labels.Single(text => text.Text == "Impact Effect:");
            var container = impact.GetLogicalAncestors().OfType<Grid>().Single(grid => Equals(grid.Tag, "lure.animations"));
            Assert.Single(container.ColumnDefinitions);
            Assert.IsType<StackPanel>(impact.Parent);
            Assert.Contains(((StackPanel)impact.Parent!).Children, child => child is AutoCompleteBox ||
                child is Grid marker && marker.GetLogicalDescendants().OfType<AutoCompleteBox>().Any());
            foreach (var width in new[] { 900d, 600d, 900d })
            {
                container.Width = width; await RoleSettle(host);
                FormLayoutAssertions.NoOverlaps(container, $"Lure visual fields/{original}/{width}");
            }
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<protoaction><name>LureAction</name><type>Lure</type><anim>DashStart</anim><reloadanim>DashEnd</reloadanim><impacteffect>effects\\impacts\\pierce</impacteffect></protoaction>"), RoleFixture);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedAbilityUsesSharedRolesWithoutChangingItsReadonlyOrLoadingPolicy(bool original)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            if (original)
            {
                host.Set("_barXmlRoot", XElement.Parse(RoleDocument()));
                host.Set("_modXmlRoot", XElement.Parse("<protomods><unit name='B'/></protomods>"));
                host.Call("BuildEditorPanel", "A", true);
            }
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 3;
            await RoleSettle(host);
            if (Field<Task?>(host, "_pendingAbilityEditorLoadTask") is { } load) await load.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 200 && (Field<bool>(host, "_loadingAbilityEditor") || Field<string>(host, "_currentAbilityEditorName") != "AbilityA"); i++) await Task.Delay(10);
            Assert.Equal("AbilityA", Field<string>(host, "_currentAbilityEditorName"));
            host.Width = 1600; await RoleSettle(host);
            var panel = Field<StackPanel>(host, "_abilitiesEditorPanel");
            var targets = panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct()
                .Where(box => box.Classes.Contains("compact-chip-picker")).ToArray();
            Assert.Equal(2, targets.Length);
            foreach (var target in targets)
            {
                Assert.Equal(50, target.Width); Assert.True(EditorFieldInteraction.IsConfigured(target));
                Assert.False(target.IsEffectivelyEnabled); // Existing Power definitions are edited through their manager.
                Assert.False(target.IsEffectivelyVisible); // Read-only chip values remain; their empty inputs do not.
            }
            var tech = panel.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => box.IsEffectivelyVisible && box.Text == RoleTech);
            AssertRole(tech, EditorFieldWidthRole.Reference); Assert.Equal(!original, tech.IsEnabled);
            var radius = panel.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(box => box.IsEffectivelyVisible && box.Text == "9");
            AssertRole(radius, EditorFieldWidthRole.Numeric); Assert.False(radius.IsEffectivelyEnabled);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), fixture =>
        {
            RoleFixture(fixture);
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/abilities/abilities_mods.xml", $"<abilitiesmods><A><ability>AbilityA<tech>{RoleTech}</tech></ability></A></abilitiesmods>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", $"<powersmod><power name='AbilityA' type='UnitAction'><unitaction>HandAttack</unitaction><radius>9</radius><placement>Unit</placement><abstractplacementtargettype>{RoleUnit}</abstractplacementtargettype><explicitlyrestrictedplacementtargettype>CheckpointType</explicitlyrestrictedplacementtargettype></power></powersmod>");
        });

    [Fact]
    public void UnitActionRolesKeepLongRatePrefixHoverSelectionAndSaveReopen()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host);
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 1;
            host.Width = 1500; await RoleSettle(host);
            var action = RemovalAction(host);
            var structured = Assignment<IDictionary>(action, "StructuredFieldRows");
            var rate = Assert.Single(((IList)structured["rate"]!).Cast<object>());
            var target = Assert.IsType<AutoCompleteBox>(Assignment<Dictionary<string, Control>>(rate, "AttributeEditors")["type"]);
            var value = Assert.IsType<TextBox>(Assignment<Control>(rate, "ValueTb"));
            AssertRole(target, EditorFieldWidthRole.Reference); AssertRole(value, EditorFieldWidthRole.Numeric);
            var bonus = Assert.Single(RemovalRows(action, "BonusRows").Cast<object>());
            AssertRole(Assignment<AutoCompleteBox>(bonus, "TypeAcb"), EditorFieldWidthRole.Reference);
            AssertRole(Assignment<TextBox>(bonus, "ValTb"), EditorFieldWidthRole.DamageBonusMultiplier);
            Assert.False(Field<bool>(host, "_isDirty"));
            target.BringIntoView(); await RoleSettle(host);
            var input = target.GetVisualDescendants().OfType<TextBox>().First();
            var scroll = input.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.InRange(scroll.Offset.X, 0, .5);
            var tip = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent); target.RaiseEvent(tip);
            Assert.False(tip.Cancel);
            var tipText = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(ToolTip.GetTip(target)).Content).Children.OfType<TextBlock>().First();
            Assert.Equal(RoleUnit, tipText.Text);
            RoleClick(host, input); await RoleSettle(host);
            Assert.Equal(RoleUnit.Length, Math.Abs(input.SelectionStart - input.SelectionEnd));
            target.SelectedItem = "B"; value.Text = "2"; await RoleSettle(host);
            host.FindControl<TextBox>("_unitNameBox")!.Focus(); await RoleSettle(host);
            Assert.InRange(scroll.Offset.X, 0, .5);
            Assert.Empty(await Save(host));
            var unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Equal("B", (string?)unit.Element("protoaction")!.Element("rate")!.Attribute("type"));
            Assert.Equal("2", unit.Element("protoaction")!.Element("rate")!.Value);
            Assert.Equal("yes", (string?)unit.Element("protoaction")!.Element("future")!.Attribute("keep"));
            host.Call("BuildEditorPanel", "A", false); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument($"<protoaction><name>HandAttack</name><type>Attack</type><rate type='{RoleUnit}'>1</rate><damage type='Hack'>2</damage><damagebonus type='{RoleUnit}'>3</damagebonus><future keep='yes'/></protoaction>"), RoleFixture);
}
