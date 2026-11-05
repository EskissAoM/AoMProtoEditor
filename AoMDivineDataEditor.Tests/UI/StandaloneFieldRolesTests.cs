using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class TacticsStartupTests
{
    private static void AssertActionRole(Control field, EditorFieldWidthRole role)
    {
        var width = EditorFormLayout.WidthFor(role);
        Assert.Equal(width, field.Width); Assert.Equal(width, field.MinWidth); Assert.Equal(width, field.MaxWidth);
        Assert.Equal(width, field.Bounds.Width); Assert.Equal(30, field.Bounds.Height);
        Assert.True(EditorFieldInteraction.IsConfigured(field));
    }

    [Theory]
    [InlineData("UnitAction")]
    [InlineData("GeneralEffect")]
    public void AbilityFieldsShareRolesAndPreserveSaveBehaviorInBothDefinitionModes(string type)
        => WithStandaloneXml(true, async (_, _, child, path, _) =>
        {
            child.Width = 1500; child.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            var chips = child.GetLogicalDescendants().OfType<AutoCompleteBox>().Where(box => box.Classes.Contains("compact-chip-picker")).ToArray();
            Assert.Equal(2, chips.Length);
            foreach (var chip in chips) AssertActionRole(chip, EditorFieldWidthRole.ChipSearch);
            if (type == "UnitAction")
            {
                var action = child.GetLogicalDescendants().OfType<AutoCompleteBox>().Single(box => box.IsEffectivelyVisible && box.Text == "HandAttack");
                AssertActionRole(action, EditorFieldWidthRole.Reference);
            }
            var radius = child.GetLogicalDescendants().OfType<TextBox>().Single(box => box.IsEffectivelyVisible && box.Text == "9");
            AssertActionRole(radius, EditorFieldWidthRole.Numeric);
            var icon = child.GetLogicalDescendants().OfType<AssetPathEditor>().Single(editor => editor.IsEffectivelyVisible);
            AssertActionRole(icon, EditorFieldWidthRole.AssetPath);
            Assert.False((bool)Field(child, "_isDirty")!);
            radius.Text = "12";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.Empty(await SaveStandaloneChecked(child, true));
            var saved = XDocument.Load(path).Root!.Element("power")!;
            Assert.Equal(type, (string?)saved.Attribute("type")); Assert.Equal("12", saved.Element("radius")!.Value);
            Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
        }, abilitySource: $"<power name='AbilityA' type='{type}'><unitaction>HandAttack</unitaction><icon>custom_icon.png</icon><radius>9</radius><placement>Unit</placement><abstractplacementtargettype>BaseUnit</abstractplacementtargettype><explicitlyrestrictedplacementtargettype>CustomType</explicitlyrestrictedplacementtargettype><future keep='yes'/></power>");

    [Fact]
    public void TacticsRateAndPriorityFieldsUseSharedRolesAndRetainRoundTrip()
        => WithStandaloneXml(false, async (_, _, child, path, _) =>
        {
            child.Width = 1500; child.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            var widget = Assert.Single(((IList)Field(child, "_protoActionWidgets")!).Cast<object>());
            var structured = (IDictionary)widget.GetType().GetProperty("StructuredFieldRows")!.GetValue(widget)!;
            var rate = Assert.Single(((IList)structured["rate"]!).Cast<object>());
            var editors = (Dictionary<string, Control>)rate.GetType().GetProperty("AttributeEditors")!.GetValue(rate)!;
            var target = Assert.IsType<AutoCompleteBox>(editors["type"]);
            AssertActionRole(target, EditorFieldWidthRole.Reference);
            var value = Assert.IsType<TextBox>(rate.GetType().GetProperty("ValueTb")!.GetValue(rate));
            AssertActionRole(value, EditorFieldWidthRole.Numeric);
            Assert.False((bool)Field(child, "_isDirty")!);
            target.SelectedItem = "CustomUnit"; value.Text = "4";
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            var modeTabs = child.GetVisualDescendants().OfType<TabControl>().Single(tabs =>
                tabs.Items.OfType<TabItem>().Any(tab => Equals(tab.Header, "Tactic")));
            modeTabs.SelectedIndex = 1;
            child.Width = 1000; child.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); child.UpdateLayout(); await Task.Delay(30);
            var priority = child.GetLogicalDescendants().OfType<TextBox>().Distinct().Single(box => box.IsEffectivelyVisible && box.Text == "100");
            AssertActionRole(priority, EditorFieldWidthRole.TacticsWeight);
            var weightRow = Assert.IsType<StackPanel>(priority.Parent);
            var cross = Assert.Single(weightRow.Children.OfType<Button>());
            var p = priority.TranslatePoint(default, child)!.Value; var c = cross.TranslatePoint(default, child)!.Value;
            Assert.InRange(c.X - p.X - priority.Bounds.Width, 3.9, 4.1);
            Assert.InRange(Math.Abs(c.Y + cross.Bounds.Height / 2 - p.Y - priority.Bounds.Height / 2), 0, .5);
            var actionChoice = Assert.Single(weightRow.Children.OfType<ComboBox>());
            AssertActionRole(actionChoice, EditorFieldWidthRole.Reference);
            Assert.True(weightRow.Bounds.Right <= ((WrapPanel)weightRow.Parent!).Bounds.Width + .5);
            Assert.Empty(await SaveStandaloneChecked(child, false));
            var saved = XDocument.Load(path);
            Assert.Equal("CustomUnit", (string?)saved.Descendants("rate").Single().Attribute("type")); Assert.Equal("4", saved.Descendants("rate").Single().Value);
            Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
        }, tacticsSource: "<tactics><action><name>HandAttack</name><type>Attack</type><rate type='BaseUnit'>1</rate><future keep='yes'/></action><tactic>Default<action priority='100'>HandAttack</action><attacktype>BaseUnit</attacktype></tactic></tactics>");
}
