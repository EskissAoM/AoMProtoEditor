using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class PowerFieldPresentationTests
{
    public static IEnumerable<object[]> DynamicPowerFamilies
    {
        get
        {
            var cases = new (string Type, string Body)[]
            {
                ("Rain", "<effect type='Damage' action='Gather' relativity='BasePercent' amount='2' damagetype='Hack'><target type='ProtoUnit'>OtherUnit</target></effect>"),
                ("UnitModify", "<unitmodify apply='Add' amount='2' damagetype='Hack' age='ClassicalAge' excludetypes='CustomType'>DamageSpecific</unitmodify>"),
                ("EffectInArea", "<onhiteffect type='Boost' active='1' radius='10' duration='3'><modify type='DamageSpecific' applytype='Multiply' damagetype='Hack'>2</modify><modifyramp type='Speed' start='1' end='2' duration='3'/><attacktype>CustomType</attacktype></onhiteffect>"),
                ("SwitchingEffects", "<tiertype>TotalResources</tiertype><effects><displaynameid>STR_POWER_A_NAME</displaynameid><resourcetierreq type='Food'>20</resourcetierreq><effect type='Damage' action='Gather' relativity='BasePercent' amount='2'><target type='ProtoUnit'>OtherUnit</target></effect></effects>"),
                ("TempUnit", "<createunit quantity='2' lifespan='3' minradius='1' maxradius='4'>OtherUnit</createunit><ageadjustment quantityaddon='1'>ClassicalAge</ageadjustment>"),
                ("TempUnit", "<createunit quantity='2' waterprotoid='OtherUnit' minradius='1' maxradius='4'>OtherUnit</createunit>"),
                ("DroughtLand", "<terraincreep creep='DroughtLand' maxradius='30'><protounit>OtherUnit</protounit></terraincreep><initialdamage fraction='0.2'>OtherUnit</initialdamage>"),
                ("GreatFlood", "<damagecap unittype='CustomType'>12.5</damagecap>"),
                ("GreatHunt", "<maxfood age='2'>30</maxfood>"),
                ("Fimbulwinter", "<snowdrift target='OtherUnit'>OtherUnit</snowdrift>"),
                ("CageUnit", "<resourcevaluemultiplier resourcetype='Favor'>10</resourcevaluemultiplier>"),
                ("Bronze", "<armor><abstracttype>CustomType</abstracttype><armortype vulnerabilitypercentdelta='-0.3'>Hack</armortype><vfxproto>OtherUnit</vfxproto></armor>"),
                ("Lure", "<ageinfo maxfood='20' spawnamount='2'>2</ageinfo>"),
                ("FoxCharm", "<ageadjustment affectedNumber='2'>ClassicalAge</ageadjustment>"),
                ("Tempest", "<fasteststriketime>1</fasteststriketime><sloweststriketime>2</sloweststriketime>"),
                ("Bolt", "<soundset type='StartSound' listenertype='All'>custom_sound.xml</soundset><placementrestriction radius='4'>CustomType</placementrestriction>"),
                ("GeneralEffect", "<effect type='OnHitEffectRate' action='Gather' effecttype='Stun' relativity='BasePercent' amount='2'><target type='ProtoUnit'>OtherUnit</target></effect>")
            };
            foreach (var item in cases)
                foreach (var original in new[] { false, true }) yield return [item.Type, item.Body, original];
        }
    }

    [Theory]
    [MemberData(nameof(DynamicPowerFamilies))]
    public void PopulatedPowerFamiliesHaveExplicitRolesGroupedCrossesAndNoOverlap(string type, string body, bool original)
        => WithPower(type, body, original, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            // Sound sets and placement restrictions are on Properties, even for Bolt.
            Field<TabControl>(view, "_powerTabs").SelectedIndex = type == "Bolt" ? 0 : 1; await Settle(window);
            var scroll = Field<ScrollViewer>(view, type == "Bolt" ? "_propertiesScroll" : "_specificScroll");
            var content = Assert.IsAssignableFrom<Control>(scroll.Content);
            foreach (var width in new[] { 980d, 680d })
            {
                scroll.Width = width; await Settle(window);
                FormLayoutAssertions.NoOverlaps(content, $"Power {type}/{original}/{width}");
            }
            var fields = content.GetLogicalDescendants().OfType<Control>().Distinct().Where(control =>
                control.IsEffectivelyVisible && (control is AutoCompleteBox or ComboBox or AssetPathEditor || control is TextBox { AcceptsReturn: false }) &&
                !control.GetLogicalAncestors().TakeWhile(parent => !ReferenceEquals(parent, content)).Any(parent => parent is AutoCompleteBox or ComboBox or AssetPathEditor)).ToArray();
            if (type != "Tempest") Assert.Contains(fields, field => string.IsNullOrEmpty(field.Name));
            foreach (var field in fields)
            {
                Assert.True(EditorFormLayout.TryGetWidthRole(field, out var role), $"{type}/{field.Name}/{field.Tag}");
                Assert.Equal(EditorFormLayout.WidthFor(role), field.Width);
                Assert.Equal(field.Width, field.MinWidth); Assert.Equal(field.Width, field.MaxWidth);
                if (field.Parent is StackPanel group)
                {
                    var at = group.Children.IndexOf(field);
                    if (at + 1 < group.Children.Count && group.Children[at + 1] is Button cross && cross.IsEffectivelyVisible &&
                        cross.Classes.Contains("remove-button") && !cross.Classes.Contains("remove-card"))
                        FormLayoutAssertions.CrossGap(field, cross, window);
                }
            }
            Assert.False(view.IsDirty, FieldEntry(view).ToString()); Assert.Equal(before, File.ReadAllBytes(path));
        });

    private static XElement FieldEntry(PowerEditorView view)
        => (XElement)typeof(PowerEditorView).GetProperty("_currentEntry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;

    [Fact]
    public void DroughtDisplayedDefaultsDoNotMutateXmlButRealNumericEditsSaveAndReopen()
        => WithPower("DroughtLand", "<terraincreep creep='DroughtLand' maxradius='30' future='kept'><protounit>OtherUnit</protounit></terraincreep>", false, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var host = Field<StackPanel>(view, "_droughtTerrainCreepHost");
            var growth = host.GetLogicalDescendants().OfType<StackPanel>().Distinct()
                .Single(group => group.Children.OfType<TextBlock>().Any(label => label.Text == "Growth Rate"))
                .Children.OfType<TextBox>().Single();
            Assert.Equal("0", growth.Text); Assert.False(view.IsDirty);
            Assert.Null(FieldEntry(view).Element("terraincreep")!.Attribute("growthrate"));
            Assert.Equal(before, File.ReadAllBytes(path));
            growth.Text = "2.5"; await Settle(window); Assert.True(view.IsDirty);
            var saving = view.SaveAsync(); string? error = null;
            for (var i = 0; !saving.IsCompleted && i < 500; i++)
            {
                if (window.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } prompt)
                { error = prompt.PromptText; prompt.Close(); }
                await Task.Delay(10);
            }
            Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(3)), error);
            var creep = XDocument.Load(path).Descendants("terraincreep").Single();
            Assert.Equal("2.5", (string?)creep.Attribute("growthrate"));
            Assert.Equal("kept", (string?)creep.Attribute("future"));
            Assert.Equal("OtherUnit", creep.Element("protounit")!.Value);
            foreach (var absent in new[] { "decayrate", "minupdateinterval", "maxupdateinterval" }) Assert.Null(creep.Attribute(absent));
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A"); await Settle(window);
            Assert.False(view.IsDirty); Assert.Equal("2.5", (string?)FieldEntry(view).Element("terraincreep")!.Attribute("growthrate"));
        });

    [Fact]
    public void RebuiltModifierAndOptionalAgeKeepRolesGapsAndSaveOwnership()
        => WithPower("UnitModify", "<unitmodify apply='Add' amount='2'>Speed</unitmodify>", false, async (view, window, path) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var rows = Field<StackPanel>(view, "_unitModifyRows");
            rows.GetLogicalDescendants().OfType<AutoCompleteBox>().Distinct().Single(box => Equals(box.Tag, "modifytype"))
                .SelectedItem = ProtoConstants.GetModifyTypeDisplayName("DamageSpecific");
            await Settle(window);
            var damage = rows.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(box => Equals(box.Tag, "damagetype"));
            Assert.Equal(100, damage.Width); damage.SelectedItem = "Pierce"; await Settle(window);
            rows.GetLogicalDescendants().OfType<Button>().Distinct().Single(button => Equals(button.Content, "Age"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle(window);
            var age = rows.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(box => Equals(box.Tag, "age"));
            Assert.Equal(180, age.Width); age.SelectedItem = "HeroicAge"; await Settle(window);
            age = rows.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(box => Equals(box.Tag, "age"));
            FormLayoutAssertions.CrossGap(age, Assert.IsType<StackPanel>(age.Parent).Children.OfType<Button>().Single(), window);
            FormLayoutAssertions.NoOverlaps(rows, "rebuilt UnitModify");
            var saving = view.SaveAsync(); string? error = null;
            for (var i = 0; !saving.IsCompleted && i < 500; i++)
            {
                if (window.OwnedWindows.OfType<Prompt>().FirstOrDefault() is { } prompt)
                { error = prompt.PromptText; prompt.Close(); }
                await Task.Delay(10);
            }
            Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(3)), error);
            var modify = XDocument.Load(path).Descendants("unitmodify").Single();
            Assert.Equal("DamageSpecific", modify.Value); Assert.Equal("Pierce", (string?)modify.Attribute("damagetype"));
            Assert.Equal("HeroicAge", (string?)modify.Attribute("age")); Assert.Equal("2", (string?)modify.Attribute("amount"));
            await view.SelectPowerAsync("B"); await view.SelectPowerAsync("A"); await Settle(window);
            Assert.False(view.IsDirty);
            Assert.Equal(100, rows.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(box => Equals(box.Tag, "damagetype")).Width);
        });
}
