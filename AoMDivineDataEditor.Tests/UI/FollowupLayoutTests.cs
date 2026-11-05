using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void ActionRemovalStaysInTheCardTopRightWhenHeaderFieldsWrap()
        => WithHost(async (_, host) =>
        {
            PrepareSnippetActions(host); Field<TabControl>(host, "_editorTabs").SelectedIndex = 1; await RoleSettle(host);
            var action = RemovalAction(host); var remove = Assignment<Button>(action, "RemoveButton");
            var header = Assert.IsType<Grid>(remove.Parent); var card = Assignment<Border>(action, "WidgetBorder");
            foreach (var width in new[] { 950d, 470d, 950d })
            {
                card.Width = width; await RoleSettle(host);
                Assert.InRange(Math.Abs(header.Bounds.Width - remove.Bounds.Right), 0, .5);
                Assert.InRange(remove.Bounds.Top, 0, .5);
                FormLayoutAssertions.NoOverlaps(header, "Action header");
            }
            Assert.False(Field<bool>(host, "_isDirty"));
        }, RoleDocument("<protoaction><name>HandAttack</name><type>Attack</type><handlogic>1</handlogic></protoaction>"), RoleFixture);
}

public sealed partial class PowerFieldPresentationTests
{
    [Fact]
    public void NewlyAddedPlacementRestrictionsHaveMeasuredGapsBeforeAnyOtherEdit()
        => WithPower("Bolt", "<placement>Full</placement><placementrestriction radius='1'>CustomType</placementrestriction>", false, async (view, window, _) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 0; await Settle(window);
            var add = Field<Button>(view, "_addPlacementRestrictionButton");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(window);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(window);
            var rows = Field<StackPanel>(view, "_placementRestrictionRows");
            Assert.Equal(3, rows.Children.OfType<WrapPanel>().Count()); Assert.Same(rows, add.Parent);
            foreach (var width in new[] { 850d, 520d, 850d })
            {
                rows.Width = width; await Settle(window);
                FormLayoutAssertions.NoOverlaps(rows, "new placement restriction rows");
                foreach (var row in rows.Children.OfType<WrapPanel>())
                foreach (var group in row.Children.OfType<StackPanel>())
                {
                    var label = Assert.Single(group.Children.OfType<TextBlock>());
                    var input = group.Children.Single(child => child is TextBox or AutoCompleteBox);
                    var p = label.TranslatePoint(default, rows)!.Value; var q = input.TranslatePoint(default, rows)!.Value;
                    Assert.InRange(q.X - p.X - label.Bounds.Width, 7.9, 8.1);
                    Assert.InRange(Math.Abs(p.Y + label.Bounds.Height / 2 - q.Y - input.Bounds.Height / 2), 0, .5);
                    if (group.Children.OfType<Button>().SingleOrDefault() is { } cross) FormLayoutAssertions.CrossGap(input, cross, rows);
                }
            }
        });
}
