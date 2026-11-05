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
    [Fact]
    public void UnitR6SocketAndMinimapUseSharedColumnsAndKeepSizeBesideColors()
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var before = XDocument.Load(fixture.ProtoPath);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            Assert.Equal("B", Assert.IsType<AutoCompleteBox>(fields["nonsocketplaceprotoid"]).Text);
            var expected = fields["socketunittype"].TranslatePoint(default, host)!.Value.X;
            Assert.InRange(Math.Abs(fields["nonsocketplaceprotoid"].TranslatePoint(default, host)!.Value.X - expected), 0, .5);
            Assert.False(Field<bool>(host, "_isDirty"));
            var module = StatsModule(host, "minimapvisuals");
            var sizeAdd = Assert.Single(module.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Size"));
            sizeAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            var size = Assert.IsType<TextBox>(fields["minimapsize"]);
            var colorsAdd = Assert.Single(module.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, "Colors"));
            Assert.InRange(Math.Abs(size.TranslatePoint(default, host)!.Value.Y - colorsAdd.TranslatePoint(default, host)!.Value.Y), 0, .5);
            size.Text = "2";
            colorsAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            size = (TextBox)fields["minimapsize"];
            foreach (var key in new[] { "minimapcolor.red", "minimapcolor.green", "minimapcolor.blue" })
                ((TextBox)fields[key]).Text = "128";
            host.Width = 1000; await RoleSettle(host);
            var optional = Assert.IsType<WrapPanel>(size.Parent!.Parent!.Parent);
            foreach (var group in optional.Children) Assert.True(group.Bounds.Right <= optional.Bounds.Width + .5);
            Assert.Equal("B", Assert.IsType<AutoCompleteBox>(fields["nonsocketplaceprotoid"]).Text);
            await RoleSettle(host); Assert.Empty(await Save(host));
            var saved = XDocument.Load(fixture.ProtoPath);
            var unit = saved.Root!.Element("unit")!;
            Assert.Equal("2", unit.Element("minimapsize")!.Value);
            Assert.Equal("128", (string?)unit.Element("minimapcolor")!.Attribute("blue"));
            Assert.Equal("CheckpointType", unit.Element("socketunittype")!.Value);
            Assert.True(unit.Element("nonsocketplaceprotoid")?.Value == "B", unit.ToString());
            Assert.True(XNode.DeepEquals(before.Root!.Element("unit")!.Element("future"), unit.Element("future")));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            foreach (var key in new[] { "minimapsize", "minimapcolor.blue" })
                Assert.Single(Assert.IsType<StackPanel>(fields[key].Parent).Children.OfType<Button>())
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await RoleSettle(host); Assert.Empty(await Save(host));
            unit = XDocument.Load(fixture.ProtoPath).Root!.Element("unit")!;
            Assert.Null(unit.Element("minimapsize")); Assert.Null(unit.Element("minimapcolor"));
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<nonsocketplaceprotoid>B</nonsocketplaceprotoid><minimapicon>custom_minimap</minimapicon>"), RoleFixture);

    [Fact]
    public void UnitR6VeterancyUsesAvailableChipWidthAndSharedSelectionSaveBehavior()
        => WithHost(async (fixture, host) =>
        {
            host.Width = 1900; await RoleSettle(host);
            var module = StatsModule(host, "veterancydata");
            var exclude = Assert.Single(module.GetLogicalDescendants().OfType<StackPanel>(), panel =>
                panel.Children.FirstOrDefault() is StackPanel header && header.Children.OfType<TextBlock>().Any(t => t.Text == "Exclude Types"));
            var chips = Assert.Single(exclude.Children.OfType<WrapPanel>());
            Assert.True(double.IsNaN(chips.Width));
            Assert.Equal(2, chips.Children.Count);
            Assert.InRange(Math.Abs(chips.Children[0].Bounds.Y - chips.Children[1].Bounds.Y), 0, .5);
            host.Width = 1000; await RoleSettle(host);
            var grid = Assert.IsType<Grid>(exclude.Parent);
            Assert.True(exclude.Bounds.Right <= grid.Bounds.Width + .5);
            foreach (var chip in chips.Children) Assert.True(chip.Bounds.Right <= chips.Bounds.Width + .5);
            Assert.False(Field<bool>(host, "_isDirty"));
            var picker = Assert.Single(exclude.GetLogicalDescendants().OfType<AutoCompleteBox>());
            AssertRole(picker, EditorFieldWidthRole.ChipSearch);
            picker.SelectedItem = "CheckpointType"; await RoleSettle(host);
            Assert.Contains("CheckpointType", Field<HashSet<string>>(host, "_currentVeterancyExcludeTypes"));
            Assert.Empty(await Save(host));
            Assert.Contains("CheckpointType", XDocument.Load(fixture.ProtoPath).Descendants("veterancybonus").Single().Element("excludetypes")!.Elements().Select(e => e.Value));
            host.Call("BuildEditorPanel", "A", true); await RoleSettle(host);
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<veterancyranks><rank><numkills>2</numkills></rank></veterancyranks><veterancybonus><rank id='1'><veterancymodify modifytype='Damage'>1.2</veterancymodify></rank><includetypes><unittype>Unit</unittype></includetypes><excludetypes><unittype>A</unittype><unittype>B</unittype></excludetypes></veterancybonus>"), RoleFixture);
}
