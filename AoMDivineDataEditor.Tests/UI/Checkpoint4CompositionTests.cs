using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    public static IEnumerable<object[]> OnHitLayoutTypes => ProtoConstants.KnownOnHitEffectTypes.Select(type => new object[] { type });

    private static string LayoutEffect(string type) => $"""
        <onhiteffect type="{type}" duration="5" delay="1" radius="4" prob="0.5" globalprob="0.5" stacklimit="3" rate="0.5" amount="2">
        <attacktype>{RoleUnit}</attacktype><ignoretype>CheckpointType</ignoretype>
        <modify type="DamageSpecific" applytype="Multiply" dmgtype="Hack">2</modify>
        <modify type="Speed" applytype="Add">1</modify>
        <modifyramp type="DamageSpecific" dmgtype="Hack" init="1" final="2"/>
        <damage type="Hack">2</damage></onhiteffect>
        """;

    [Theory]
    [MemberData(nameof(OnHitLayoutTypes))]
    public void EveryOnHitTypeHasNonOverlappingCompleteGroupsAtBothWidths(string type)
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var effect = Assert.Single(RemovalRows(RemovalAction(host), "OnHitEffectRows").Cast<object>());
            Assert.True(Assignment<bool>(effect, "IsSupported"));
            var card = Assignment<Border>(effect, "Card");
            var before = File.ReadAllBytes(fixture.ProtoPath);
            foreach (var width in new[] { 880d, 560d })
            {
                card.Width = width; await RoleSettle(host);
                FormLayoutAssertions.NoOverlaps(card, $"{type} / {width}");
            }
            foreach (var modifier in Assignment<IList>(effect, "ModifyRows").Cast<object>())
            {
                AssertRole(Assignment<ComboBox>(modifier, "ApplyTypeCb"), EditorFieldWidthRole.ShortEnum);
                var selector = Assignment<AutoCompleteBox>(modifier, "ModifyTypeAcb");
                selector.Text = "ArmorSpecific"; await RoleSettle(host);
                FormLayoutAssertions.NoOverlaps(card, $"{type} / DamageSpecific");
                selector.Text = "Speed"; await RoleSettle(host);
                FormLayoutAssertions.NoOverlaps(card, $"{type} / ordinary modifier");
            }
            if (type is "StatModify" or "SelfModify" or "Boost")
            {
                Assert.Equal(2, Assignment<IList>(effect, "ModifyRows").Count);
                Assert.Single(Assignment<IList>(effect, "ModifyRampRows").Cast<object>());
                foreach (var (caption, stateName) in new[] { ("Modifier", "ModifyRows"), ("Modifier Ramp", "ModifyRampRows") })
                {
                    var rows = Assignment<IList>(effect, stateName); var count = rows.Count;
                    var add = card.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, caption));
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
                    Assert.Equal(count + 1, rows.Count);
                    FormLayoutAssertions.NoOverlaps(card, $"{type} / added {caption}");
                    var added = rows[rows.Count - 1]!;
                    var remove = Assignment<Control>(added, "RowPanel").GetLogicalDescendants().OfType<Button>()
                        .Single(button => button.Classes.Contains("remove-button"));
                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
                    Assert.Equal(count, rows.Count);
                    FormLayoutAssertions.NoOverlaps(card, $"{type} / removed {caption}");
                }
            }
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument($"<protoaction><name>HandAttack</name><type>Attack</type>{LayoutEffect(type)}</protoaction>"), RoleFixture);

    [Fact]
    public void EmbeddedAbilityHeaderRechargeAndAssociationFieldsWrapWithoutCollisions()
        => WithHost(async (fixture, host) =>
        {
            Field<TabControl>(host, "_editorTabs").SelectedIndex = 3; await RoleSettle(host);
            if (Field<Task?>(host, "_pendingAbilityEditorLoadTask") is { } load) await load.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 200 && (Field<bool>(host, "_loadingAbilityEditor") || Field<string>(host, "_currentAbilityEditorName") != "AbilityA"); i++) await Task.Delay(10);
            Assert.Equal("AbilityA", Field<string>(host, "_currentAbilityEditorName"));
            var name = Field<AutoCompleteBox>(host, "_currentAbilityNameEditor");
            var card = name.GetVisualAncestors().OfType<Border>().First(border => border.Child is StackPanel);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var recharge = card.GetVisualDescendants().OfType<ComboBox>().Single(box => Equals(box.SelectedItem, "Aux recharge"));
            Assert.NotNull(recharge);
            foreach (var width in new[] { 940d, 620d })
            {
                card.Width = width; await RoleSettle(host);
                var header = name.GetVisualAncestors().OfType<Grid>().First(grid => grid.Children.Contains(name.GetVisualAncestors().OfType<WrapPanel>().First()));
                FormLayoutAssertions.NoOverlaps(header, $"embedded Ability header / {width}");
                FormLayoutAssertions.NoOverlaps(card, "embedded Ability complete card");
                var binding = card.GetVisualDescendants().OfType<AutoCompleteBox>().Single(box =>
                    box.IsEffectivelyVisible && box.Parent is StackPanel group &&
                    group.Children.OfType<TextBlock>().Any(label => label.Text == "Bind to unit action"));
                FormLayoutAssertions.NoOverlaps(binding.GetVisualAncestors().OfType<WrapPanel>().First(), $"Ability recharge / {width}");
                var buttons = header.Children.OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                Assert.Equal(2, buttons.Length);
                var firstY = name.TranslatePoint(default, card)!.Value.Y;
                foreach (var button in buttons)
                    Assert.InRange(Math.Abs(button.TranslatePoint(default, card)!.Value.Y - firstY), 0, .5);
                var associationFields = card.GetVisualDescendants().OfType<StackPanel>().Where(row =>
                    row.Children.OfType<Button>().Any(button => button.Classes.Contains("remove-field")) &&
                    row.Children.OfType<TextBlock>().Any(label => label.Text is "Enabled by tech" or "ROF" or "Force to column")).ToArray();
                Assert.Equal(3, associationFields.Length);
                foreach (var row in associationFields)
                {
                    FormLayoutAssertions.NoOverlaps(row, "Ability association field");
                    FormLayoutAssertions.CrossGap(row.Children.OfType<Control>().Single(control => control is TextBox or AutoCompleteBox or ComboBox),
                        Assert.Single(row.Children.OfType<Button>()), card);
                }
            }
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument("<protoaction><name>HandAttack</name><type>Attack</type></protoaction><auxrechargetime>10</auxrechargetime>"), fixture =>
        {
            RoleFixture(fixture);
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/abilities/abilities_mods.xml", $"<abilitiesmods><A><ability>AbilityA<tech>{RoleTech}</tech><rof>5</rof><columnoverride>2</columnoverride><bindtoauxchargeaction>1</bindtoauxchargeaction></ability></A></abilitiesmods>");
            fixture.Workspace.WriteXml("TestMod/game/data/gameplay/powers_mods.xml", "<powersmod><power name='AbilityA' type='GeneralEffect'><placement>Unit</placement></power></powersmod>");
        });

    [Fact]
    public void AreaSortUsesOneEditorAndRetainsTheStandaloneGenericField()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var state = RemovalAction(host); var card = Assignment<Border>(state, "WidgetBorder");
            var sort = Assert.Single(card.GetLogicalDescendants().OfType<ComboBox>().Distinct(), combo =>
                combo.Items.Cast<object>().OfType<string>().Contains("Directional"));
            Assert.Same(sort, Assignment<Dictionary<string, Control>>(state, "AdditionalFieldControls")["areasortmode"]);
            AssertRole(sort, EditorFieldWidthRole.ShortEnum);
            Assert.False(Field<bool>(host, "_isDirty"));
            sort.SelectedItem = "Directional"; await RoleSettle(host); Assert.Empty(await Save(host));
            Assert.Equal("Directional", XDocument.Load(fixture.ProtoPath).Descendants("areasortmode").Single().Value);
        }, RoleDocument("<protoaction><name>Throw</name><type>Throw</type><areasortmode>Radial</areasortmode></protoaction>"), RoleFixture);

    [Fact]
    public void DamageRateBonusAndSourceWrappedCheckboxHaveNoOverlapAndRespectRowSpacing()
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var state = RemovalAction(host); var card = Assignment<Border>(state, "WidgetBorder");
            ComboBox? areaSort = null;
            foreach (var width in new[] { 950d, 620d })
            {
                card.Width = width; await RoleSettle(host);
                FormLayoutAssertions.NoOverlaps(card, $"damage/rate/bonus / {width}");
                var bonuses = RemovalRows(state, "BonusRows").Cast<object>().ToArray();
                var firstX = Assignment<AutoCompleteBox>(bonuses[0], "TypeAcb").TranslatePoint(default, card)!.Value.X;
                foreach (var bonus in bonuses)
                {
                    var target = Assignment<AutoCompleteBox>(bonus, "TypeAcb");
                    var amount = Assignment<TextBox>(bonus, "ValTb");
                    var cross = Assert.Single(Assignment<Panel>(bonus, "RowPanel").Children.OfType<Button>());
                    FormLayoutAssertions.CrossGap(amount, cross, card);
                    if (target.TranslatePoint(default, card)!.Value.Y > Assignment<AutoCompleteBox>(bonuses[0], "TypeAcb").TranslatePoint(default, card)!.Value.Y)
                        Assert.InRange(Math.Abs(target.TranslatePoint(default, card)!.Value.X - firstX), 0, .5);
                }
                var structured = Assignment<IDictionary>(state, "StructuredFieldRows");
                foreach (var rate in ((IList)structured["rate"]!).Cast<object>())
                {
                    var cross = Assert.Single(Assignment<Panel>(rate, "RowPanel").Children.OfType<Button>(), b => b.Classes.Contains("remove-button"));
                    FormLayoutAssertions.CrossGap(Assignment<Control>(rate, "ValueTb"), cross, card);
                }
                var area = Assignment<Dictionary<string, Control>>(state, "AdditionalFieldControls")["damagearea"];
                var areaPair = area.GetVisualAncestors().OfType<StackPanel>().First(panel => panel.Children.OfType<Button>().Any());
                FormLayoutAssertions.CrossGap(areaPair.Children[0], Assert.Single(areaPair.Children.OfType<Button>()), card);
                var sortGroup = card.GetLogicalDescendants().OfType<StackPanel>().Single(panel =>
                    panel.Children.OfType<TextBlock>().Any(label => label.Text == "Area Sort:"));
                var sortPair = Assert.Single(sortGroup.Children.OfType<StackPanel>());
                areaSort = Assert.Single(sortPair.GetLogicalDescendants().OfType<ComboBox>());
                Assert.Single(card.GetLogicalDescendants().OfType<ComboBox>().Distinct(), combo =>
                    combo.Items.Cast<object>().OfType<string>().Contains("Directional"));
                Assert.Same(areaSort, Assignment<Dictionary<string, Control>>(state, "AdditionalFieldControls")["areasortmode"]);
                FormLayoutAssertions.CrossGap(sortPair.Children[0], Assert.Single(sortPair.Children.OfType<Button>()), card);
            }
            var fields = Assignment<Dictionary<string, Control>>(state, "AdditionalFieldControls");
            Assert.IsType<TextBox>(fields["damagearea"]).Text = "4";
            areaSort!.SelectedItem = "Directional";
            await RoleSettle(host); Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!.Element("protoaction")!;
            Assert.Equal("4", saved.Element("damagearea")!.Value);
            Assert.Equal("Directional", saved.Element("areasortmode")!.Value);
            Assert.Equal(3, saved.Elements("damage").Count());
            Assert.Equal(3, saved.Elements("damagebonus").Count());
            Assert.Equal(2, saved.Elements("rate").Count());
        }, RoleDocument("<protoaction><name>Throw</name><type>Throw</type><maxrange>12</maxrange><damage type='Hack'>1</damage><damage type='Pierce'>2</damage><damage type='Crush'>3</damage><rate type='Unit'>1</rate><rate type='All'>1</rate><damagebonus type='A'>1</damagebonus><damagebonus type='B'>2</damagebonus><damagebonus type='Unit'>3</damagebonus><damagearea>3</damagearea><areasortmode>Radial</areasortmode></protoaction>"), RoleFixture);
}
