using System.Collections;
using System.Xml.Linq;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class CompactRowsPanelTests
{
    [Fact]
    public void AddRateSharesAnOddCompactLineButNeverCreatesAThirdEntryOrJoinsARichRow()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var rich = new Border { Width = 250, Height = 30 };
            var first = new Border { Width = 200, Height = 30 };
            var second = new Border { Width = 200, Height = 30 };
            var add = new Button { Content = "Add Rate", Classes = { "add-component" } };
            var panel = new CompactRowsPanel(row => !ReferenceEquals(row, rich)) { Width = 1000, Children = { first, add } };
            var window = new Window { Width = 1100, Height = 250, Content = panel };
            async Task Layout() { await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            try
            {
                window.Show(); await Layout(); Assert.Equal(first.Bounds.Y, add.Bounds.Y);
                panel.Children.Insert(1, second); await Layout(); Assert.True(add.Bounds.Y > second.Bounds.Y);
                panel.Children.Remove(first); panel.Children.Remove(second); panel.Children.Insert(0, rich);
                await Layout(); Assert.True(add.Bounds.Y > rich.Bounds.Y);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void PackingCapsAtTwoAndRespectsFullRowsResizeAndRemoval()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var rich = new Border { Width = 250, Height = 30 };
            var panel = new CompactRowsPanel(row => !ReferenceEquals(row, rich)) { Width = 1000 };
            var rows = Enumerable.Range(0, 5).Select(_ => new Border { Width = 200, Height = 30 }).ToArray();
            foreach (var row in rows) panel.Children.Add(row);
            panel.Children.Insert(2, rich);
            var window = new Window { Width = 1100, Height = 500, Content = panel };
            async Task Layout()
            {
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
            }
            try
            {
                window.Show(); await Layout();
                Assert.Equal(rows[0].Bounds.Y, rows[1].Bounds.Y);
                Assert.True(rich.Bounds.Y > rows[1].Bounds.Y);
                Assert.True(rows[2].Bounds.Y > rich.Bounds.Y);
                Assert.Equal(rows[2].Bounds.Y, rows[3].Bounds.Y);
                Assert.True(rows[4].Bounds.Y > rows[3].Bounds.Y);
                panel.Width = 390; await Layout();
                Assert.Equal(6, panel.Children.Select(row => row.Bounds.Y).Distinct().Count());
                panel.Children.Remove(rich); panel.Width = 1000; await Layout();
                Assert.All(panel.Children.GroupBy(row => row.Bounds.Y), line => Assert.InRange(line.Count(), 1, 2));
                Assert.Equal(rows[0].Bounds.Y, rows[1].Bounds.Y);
            }
            finally { window.Close(); }
        });
}

public sealed partial class UnitXmlEditingTests
{
    [Theory]
    [InlineData("Attack")]
    [InlineData("NoWork")]
    public void SimpleRatesPackByTwoRichRatesStaySeparateAndFlagsUseChipSearch(string type)
        => WithHost(async (fixture, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var state = RemovalAction(host); var card = Assignment<Border>(state, "WidgetBorder");
            var rows = ((IList)Assignment<IDictionary>(state, "StructuredFieldRows")["rate"]!).Cast<object>().ToArray();
            Assert.Equal(4, rows.Length);
            var panels = rows.Select(row => Assignment<Panel>(row, "RowPanel")).ToArray();
            Assert.All(panels, row => Assert.IsType<CompactRowsPanel>(row.Parent));
            card.Width = 1100; await RoleSettle(host);
            Assert.Equal(panels[0].Bounds.Y, panels[1].Bounds.Y);
            Assert.True(panels[2].Bounds.Y > panels[1].Bounds.Y);
            Assert.True(panels[3].Bounds.Y > panels[2].Bounds.Y);
            Assert.All(panels.GroupBy(row => row.Bounds.Y), line => Assert.InRange(line.Count(), 1, 2));
            card.Width = 620; await RoleSettle(host);
            Assert.Equal(4, panels.Select(row => row.Bounds.Y).Distinct().Count());
            var flagPicker = Assert.Single(Assignment<StackPanel>(state, "FlagsContainer").GetLogicalDescendants().OfType<AutoCompleteBox>());
            AssertRole(flagPicker, Classes.EditorFieldWidthRole.ChipSearch);
            Assert.NotEmpty(flagPicker.ItemsSource!.Cast<object>());
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            // Preserve all existing attributes when saving through the reflowed rows.
            var expected = XDocument.Load(fixture.ProtoPath).Descendants("rate").Select(rate => rate.ToString(SaveOptions.DisableFormatting)).ToArray();
            Assert.Empty(await Save(host));
            Assert.Equal(expected, XDocument.Load(fixture.ProtoPath).Descendants("rate").Select(rate => rate.ToString(SaveOptions.DisableFormatting)).ToArray());
        }, RoleDocument($"<protoaction><name>LayoutCase</name><type>{type}</type><rate type='Unit'>1</rate><rate type='All'>2</rate><rate type='B'>3</rate><rate type='A' srcproto='B'>4</rate></protoaction>"), RoleFixture);

    [Theory]
    [InlineData("_unitCommandRows", 2, "Command")]
    [InlineData("_trainCommandRows", 4, "Proto Unit")]
    [InlineData("_techCommandRows", 4, "Tech")]
    public void AssignmentHeadersAppearOnceAndFollowFirstRowRemovalAndEmptyAdd(string rowsField, int tab, string addLabel)
        => WithHost(async (fixture, host) =>
        {
            Field<TabControl>(host, "_editorTabs").SelectedIndex = tab; await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var rows = Field<IList>(host, rowsField); Assert.Equal(3, rows.Count);
            var firstPanel = Assignment<Panel>(rows[0]!, "RowPanel"); var container = Assert.IsAssignableFrom<Panel>(firstPanel.Parent);
            void Headers()
            {
                var shown = container.GetLogicalDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible && label.Classes.Contains("assignment-column-header")).ToArray();
                Assert.Equal(rows.Count == 0 ? 0 : 3, shown.Length);
                if (rows.Count > 0) Assert.All(shown, label => Assert.Contains(Assignment<Panel>(rows[0]!, "RowPanel"), label.GetLogicalAncestors()));
            }
            Headers(); Assert.False(Field<bool>(host, "_isDirty"));
            while (rows.Count > 0)
            {
                var value = Assignment<AutoCompleteBox>(rows[0]!, "ValueAcb");
                Assert.Single(Assert.IsType<StackPanel>(value.Parent).Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await RoleSettle(host); Headers();
            }
            var add = Assert.Single(container.Parent!.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Add " + addLabel));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await RoleSettle(host);
            Assert.Single(rows.Cast<object>()); Headers();
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument("<command row='1' column='1'>Eject</command><command row='2' column='1'>TestCommand</command><train row='1' column='1'>B</train><train row='2' column='1'>A</train><tech row='1' column='1'>OtherTech</tech><tech row='2' column='1'>ThirdTech</tech>"), RoleFixture);
}

public sealed partial class TacticsStartupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AbilityPlacementFlagsStartAtTheRowOriginAndReadonlyChipInputsAreHidden(bool readOnly, bool hasFlags)
        => WithStandaloneXml(true, async (_, _, child, path, _) =>
        {
            var before = File.ReadAllBytes(path);
            var placement = Assert.Single(child.GetVisualDescendants().OfType<ComboBox>(), box => box.IsEffectivelyVisible && Equals(box.SelectedItem, "Unit"));
            var label = Assert.Single(child.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Placement flags");
            var input = Assert.IsType<StackPanel>(label.Parent);
            var flags = Assert.IsType<DockPanel>(input.Parent);
            Assert.Equal(0, flags.Margin.Left);
            Assert.Equal(!readOnly || hasFlags, flags.IsEffectivelyVisible);
            if (flags.IsEffectivelyVisible)
                Assert.InRange(Math.Abs(label.TranslatePoint(default, child)!.Value.X - placement.TranslatePoint(default, child)!.Value.X), 0, .5);
            var chips = child.GetLogicalDescendants().OfType<AutoCompleteBox>().Where(box => box.Classes.Contains("compact-chip-picker")).ToArray();
            Assert.Equal(2, chips.Length);
            foreach (var picker in chips) Assert.Equal(!readOnly, picker.IsEffectivelyVisible);
            var flagPicker = Assert.Single(input.Children.OfType<AutoCompleteBox>());
            Assert.Equal(!readOnly, flagPicker.IsEffectivelyVisible);
            Assert.Contains(child.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "BaseUnit");
            Assert.Equal(hasFlags, flags.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == "allowgroundtarget"));
            Assert.False((bool)Field(child, "_isDirty")!); Assert.Equal(before, File.ReadAllBytes(path));
            if (!readOnly)
            {
                Assert.Empty(await SaveStandaloneChecked(child, true));
                var saved = XDocument.Load(path).Descendants("power").Single();
                Assert.Equal("BaseUnit", saved.Element("abstractplacementtargettype")!.Value);
                Assert.Equal(hasFlags, saved.Element("placement")!.Attribute("allowgroundtarget") != null);
            }
        }, readOnly, abilitySource: $"<power name='AbilityA' type='UnitAction'><unitaction>HandAttack</unitaction><radius>10</radius><placement enemy='' {(hasFlags ? "allowgroundtarget=''" : "")}>Unit</placement><abstractplacementtargettype>BaseUnit</abstractplacementtargettype></power>");
}
