using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Ui)]
public sealed class RemovalScopeTests
{
    [Fact]
    public void FieldRowAndCardReuseTheSameCrossWithScopeSpecificPlacement()
        => AvaloniaTestHost.Run(() =>
        {
            var field = EditorRemoveButtons.Field(); var row = EditorRemoveButtons.Row();
            var card = new OnHitEffectEditor(new AutoCompleteBox(), new CheckBox(), false, true);
            var charged = new ChargedEditor(false, true);
            var legacy = new Button { Classes = { "remove-button" } };
            var window = new Window { Content = new StackPanel { Children = { field, row, card, charged, legacy } } };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                foreach (var button in new[] { field, row, card.RemoveButton!, charged.RemoveButton! })
                {
                    Assert.Contains("remove-button", button.Classes); Assert.Equal("×", button.Content);
                    Assert.Equal(28, button.Bounds.Width); Assert.Equal(28, button.Bounds.Height);
                    Assert.Equal(18, button.FontSize); Assert.Equal(FontWeight.SemiBold, button.FontWeight);
                }
                Assert.Contains("remove-field", field.Classes); Assert.Equal(4, field.Margin.Left);
                Assert.Contains("remove-row", row.Classes); Assert.Equal(4, row.Margin.Left);
                foreach (var (button, description) in new[] { (field, "Remove field"), (row, "Remove row"),
                    (card.RemoveButton!, "Remove effect"), (charged.RemoveButton!, "Remove charged block"), (legacy, "Remove item") })
                {
                    Assert.Equal(description, ToolTip.GetTip(button));
                    Assert.Equal(description, AutomationProperties.GetName(button));
                }
                Assert.Same(card.Header, card.RemoveButton!.Parent);
                Assert.Equal(HorizontalAlignment.Right, card.RemoveButton.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Top, card.RemoveButton.VerticalAlignment);
                Assert.Contains("remove-card", card.RemoveButton.Classes);
                Assert.Null(new OnHitEffectEditor(new AutoCompleteBox(), new CheckBox(), true, true).RemoveButton);
                Assert.Null(new ChargedEditor(true, true).RemoveButton);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void ModifierCrossWrapsWithItsValueAndRetainsTheCompactGap()
        => AvaloniaTestHost.Run(() =>
        {
            var modify = new TextBox { Width = 170 }; var value = new TextBox { Width = 80 };
            var editor = new ModifierEditor(modify, value) { Width = 250 };
            var window = new Window { Width = 290, Height = 240, Content = editor };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var remove = editor.RemoveButton!;
                Assert.NotSame(editor, remove.Parent);
                var parent = Assert.IsType<StackPanel>(remove.Parent);
                Assert.Contains(value, parent.GetLogicalDescendants());
                var valuePoint = value.TranslatePoint(default, editor)!.Value;
                var removePoint = remove.TranslatePoint(default, editor)!.Value;
                var modifyPoint = modify.TranslatePoint(default, editor)!.Value;
                Assert.True(removePoint.Y > modifyPoint.Y);
                Assert.InRange(Math.Abs(valuePoint.Y + value.Bounds.Height / 2 - removePoint.Y - remove.Bounds.Height / 2), 0, 1);
                Assert.InRange(removePoint.X - valuePoint.X - value.Bounds.Width, 3.9, 4.1);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void ChipRemovalStaysCompactAndOnlyCallsItsOwnCallback()
        => AvaloniaTestHost.Run(() =>
        {
            var removed = 0;
            var chip = EditorChipService.CreateBlueChip("Target", () => removed++);
            var original = EditorChipService.CreateBlueChip("Original", () => removed++, readOnly: true);
            var window = new Window { Content = new WrapPanel { Children = { chip, original } } };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var button = Assert.Single(chip.GetLogicalDescendants().OfType<Button>());
                Assert.Contains("chip-remove-button", button.Classes); Assert.DoesNotContain("remove-button", button.Classes);
                Assert.Equal("Remove selection", ToolTip.GetTip(button));
                Assert.Equal("Remove selection", AutomationProperties.GetName(button));
                Assert.True(button.Bounds.Width < 28); Assert.True(button.Bounds.Height < 28);
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                Assert.Empty(original.GetLogicalDescendants().OfType<Button>());
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, removed);
            }
            finally { window.Close(); }
        });
}
