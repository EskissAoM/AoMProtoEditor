using AoMDivineDataEditor.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class SmallDialogInteractionTests
{
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Cancel(Window window, string route)
    {
        if (route == "escape") Press(window, Key.Escape);
        else if (route == "x") window.Close();
        else window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void AssertActions(Window window, string primary)
    {
        var cancel = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"));
        var confirm = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, primary));
        Assert.True(cancel.IsCancel); Assert.False(cancel.IsDefault); Assert.True(confirm.IsDefault);
        window.UpdateLayout();
        Assert.True(cancel.TranslatePoint(default, window)!.Value.X < confirm.TranslatePoint(default, window)!.Value.X);
    }

    private static void WithDialog(Func<Window> create, Func<Window, Task> check)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var owner = new Window { Width = 800, Height = 700 };
            owner.Show(); var dialog = create();
            try
            {
                var showing = dialog.ShowDialog(owner);
                Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout();
                await check(dialog);
                await showing.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally { dialog.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData("escape")] [InlineData("cancel")] [InlineData("x")]
    public void InputCancellationDoesNotReturnTheEditedName(string route)
        => WithDialog(() => new InputPromptWindow("Rename:", "Old", "Save"), window =>
        {
            var dialog = Assert.IsType<InputPromptWindow>(window);
            var input = dialog.FindControl<TextBox>("_inputBox")!;
            Assert.True(input.IsKeyboardFocusWithin); AssertActions(dialog, "Save");
            input.Text = "New"; Cancel(dialog, route);
            Assert.False(dialog.IsVisible); Assert.Null(dialog.InputText);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(true, "Changed name")] [InlineData(false, "Changedname")]
    public void InputEnterRetainsTheExistingWhitespacePolicy(bool whitespace, string expected)
        => WithDialog(() => new InputPromptWindow("Name:", "Old", "Save", whitespace), window =>
        {
            var dialog = Assert.IsType<InputPromptWindow>(window);
            dialog.FindControl<TextBox>("_inputBox")!.Text = " Changed name ";
            Press(dialog, Key.Enter);
            Assert.False(dialog.IsVisible); Assert.Equal(expected, dialog.InputText);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("escape")] [InlineData("cancel")] [InlineData("x")]
    public void PickerCancellationDoesNotReturnTheHighlightedItem(string route)
        => WithDialog(() => new PickerWindow("Pick", ["Alpha", "Beta"], 1), window =>
        {
            var dialog = Assert.IsType<PickerWindow>(window);
            AssertActions(dialog, "Confirm"); Assert.Equal("Beta", dialog.SelectedItem?.Display);
            Assert.True(dialog.FindControl<TextBox>("_filterBox")!.IsKeyboardFocusWithin);
            Cancel(dialog, route);
            Assert.False(dialog.IsVisible); Assert.Null(dialog.Picked); Assert.Null(dialog.PickedIndex);
            return Task.CompletedTask;
        });

    [Fact]
    public void PickerEnterReturnsTheFilteredItemAndItsOriginalIndex()
        => WithDialog(() => new PickerWindow("Pick", ["Alpha", "Beta", "Gamma"], null), window =>
        {
            var dialog = Assert.IsType<PickerWindow>(window);
            dialog.FindControl<TextBox>("_filterBox")!.Text = "Gamma";
            Dispatcher.UIThread.RunJobs(); Assert.Single(dialog.FilteredItems);
            Press(dialog, Key.Enter);
            Assert.False(dialog.IsVisible); Assert.Equal("Gamma", dialog.PickedItem); Assert.Equal(2, dialog.PickedIndex);
            return Task.CompletedTask;
        });

    [Fact]
    public void EmptyPickerCannotConfirmAndEscapeStillCancels()
        => WithDialog(() => new PickerWindow("Pick", ["Alpha"], null), window =>
        {
            var dialog = Assert.IsType<PickerWindow>(window);
            dialog.FindControl<TextBox>("_filterBox")!.Text = "NoMatch"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(dialog.FilteredItems); Assert.Null(dialog.SelectedItem);
            Assert.False(dialog.FindControl<Button>("_confirmBtn")!.IsEnabled);
            Press(dialog, Key.Enter); Assert.True(dialog.IsVisible); Assert.Null(dialog.Picked);
            Press(dialog, Key.Escape); Assert.False(dialog.IsVisible); Assert.Null(dialog.PickedIndex);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("escape")] [InlineData("cancel")] [InlineData("x")]
    public void NewUnitTypeCancellationDoesNotReturnAType(string route)
        => WithDialog(() => new NewUnitTypeWindow(), window =>
        {
            var dialog = Assert.IsType<NewUnitTypeWindow>(window);
            AssertActions(dialog, "OK");
            var selector = dialog.FindControl<ComboBox>("_typeComboBox")!;
            Assert.True(selector.IsKeyboardFocusWithin); selector.SelectedIndex = 1;
            Cancel(dialog, route); Assert.False(dialog.IsVisible); Assert.Null(dialog.SelectedType);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(0, "Unit")] [InlineData(1, "Building")] [InlineData(2, "Other")]
    public void NewUnitTypeEnterReturnsOnlyTheChosenType(int index, string expected)
        => WithDialog(() => new NewUnitTypeWindow(), window =>
        {
            var dialog = Assert.IsType<NewUnitTypeWindow>(window);
            dialog.FindControl<ComboBox>("_typeComboBox")!.SelectedIndex = index;
            // Enter on the focused ComboBox opens its list; accept from the OK
            // action after choosing, without overriding standard selector keys.
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.IsDefault).Focus();
            Press(dialog, Key.Enter); Assert.False(dialog.IsVisible); Assert.Equal(expected, dialog.SelectedType);
            return Task.CompletedTask;
        });

    [Fact]
    public void NewUnitTypeEscapeClosesTheDropdownBeforeTheDialog()
        => WithDialog(() => new NewUnitTypeWindow(), window =>
        {
            var dialog = Assert.IsType<NewUnitTypeWindow>(window);
            var selector = dialog.FindControl<ComboBox>("_typeComboBox")!;
            Press(dialog, Key.Enter); Assert.True(dialog.IsVisible); Assert.Null(dialog.SelectedType);
            Assert.True(selector.IsDropDownOpen);
            Press(dialog, Key.Escape);
            Assert.True(dialog.IsVisible); Assert.False(selector.IsDropDownOpen); Assert.Null(dialog.SelectedType);
            Press(dialog, Key.Escape); Assert.False(dialog.IsVisible); Assert.Null(dialog.SelectedType);
            return Task.CompletedTask;
        });
}
