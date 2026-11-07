using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorFieldInteractionTests
{
    private const string LongName = "AbstractCloseCombatShipWithAnExtremelyLongCustomSuffix";
    private static async Task Settle()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(25);
    }
    private static TextBox Input(Control owner) => owner as TextBox ?? owner.GetVisualDescendants().OfType<TextBox>().First();
    private static ScrollViewer Scroll(TextBox box) => box.GetVisualDescendants().OfType<ScrollViewer>().First();
    private static Point At(Window window, Control control, double fraction = .65)
        => control.TranslatePoint(new Point(control.Bounds.Width * fraction, control.Bounds.Height / 2), window)!.Value;
    private static void Click(Window window, Control control, double fraction = .65)
    {
        var point = At(window, control, fraction);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void WithField(Func<Control> create, Func<Window, Control, TextBox, Task> check, double width = 520)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var field = create();
            var sink = new TextBox { Width = 100, Text = "Other field", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            var content = new StackPanel { Margin = new Thickness(12), Spacing = 16, Children = { sink, field } };
            var window = new Window { Width = width, Height = 280, Content = content };
            try
            {
                window.Show(); sink.Focus(); await Settle(); window.UpdateLayout();
                await check(window, field, sink);
            }
            finally { window.Close(); }
        });
    private static TextBox TextField(EditorFieldWidthRole role = EditorFieldWidthRole.CompactReference)
        => EditorTextFieldStyle.ConfigureTextBox(new TextBox { Text = LongName }, role);
    private static AutoCompleteBox Catalog(Action<string>? committed = null)
    {
        var box = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox(), EditorFieldWidthRole.CompactReference);
        EditorAutoCompleteService.ConfigureStrict(box, [LongName, "CustomUnit", "Horse"], "", deferSelectionCommit: true,
            valueCommitted: committed);
        return box;
    }
    private static CancelRoutedEventArgs OpenTip(Control owner)
    {
        var args = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
        owner.RaiseEvent(args);
        return args;
    }
    private static TextBlock ValueTip(Control owner)
        => Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(ToolTip.GetTip(owner)).Content).Children[0]);

    [Fact]
    public void FirstPointerEntrySelectsAllAtTheStartButSecondClickAndDragKeepNormalSelection()
        => WithField(() => TextField(), async (window, owner, sink) =>
        {
            var input = Input(owner);
            Click(window, input); await Settle();
            Assert.Equal(LongName.Length, Math.Abs(input.SelectionEnd - input.SelectionStart));
            Assert.InRange(Scroll(input).Offset.X, 0, .5);
            Click(window, input, .2); await Settle();
            Assert.Equal(input.SelectionStart, input.SelectionEnd);
            Assert.True(input.CaretIndex > 0);
            sink.Focus(); await Settle();
            var from = At(window, input, .15); var to = At(window, input, .7);
            window.MouseDown(from, MouseButton.Left); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left);
            await Settle();
            Assert.InRange(Math.Abs(input.SelectionEnd - input.SelectionStart), 1, LongName.Length - 1);
            Assert.Equal(LongName, input.Text);
        });

    [Fact]
    public void NumericFirstClickReplacesTheValueWithoutAnEditFromFocusing()
        => WithField(() => EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox { Text = "10.25" }, EditorFieldWidthRole.Numeric),
            async (window, owner, sink) =>
            {
                var input = Input(owner); var changes = 0;
                input.TextChanged += (_, _) => changes++;
                Click(window, input); await Settle();
                Assert.Equal(5, Math.Abs(input.SelectionEnd - input.SelectionStart)); Assert.Equal(0, changes);
                window.KeyTextInput("7"); await Settle();
                Assert.Equal("7", input.Text);
                sink.Focus(); await Settle(); Assert.Equal("7", input.Text);
            });

    [Theory]
    [InlineData(520)]
    [InlineData(280)]
    public void EmptyCatalogShowsAvailableChoicesInAWiderBoundedPopupAndCommitsTheChosenValue(double width)
    {
        var commits = new List<string>();
        WithField(() => Catalog(commits.Add), async (window, owner, _) =>
        {
            var box = Assert.IsType<AutoCompleteBox>(owner);
            EditorFieldInteraction.Configure(box, EditorFieldWidthRole.CompactReference);
            EditorAutoCompleteService.EnableDropdown(box);
            var openings = 0; box.DropDownOpening += (_, _) => openings++;
            Click(window, Input(box)); await Settle();
            Assert.True(box.IsDropDownOpen); Assert.Equal(1, openings);
            var popup = box.GetTemplateDescendants().OfType<Popup>().Single();
            var popupBody = Assert.IsAssignableFrom<Control>(popup.Child);
            var list = popupBody.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.Equal(3, list.ItemCount);
            Assert.True(popupBody.Bounds.Width >= Math.Min(320, window.ClientSize.Width - 16) - .5);
            Assert.True(popupBody.Bounds.Width <= window.ClientSize.Width - 16 + .5);
            Assert.Equal(150, box.Bounds.Width); Assert.Empty(commits);
            box.SelectedItem = LongName; await Settle();
            Assert.Equal(LongName, box.Text); Assert.Equal([LongName], commits);
            Assert.InRange(Scroll(Input(box)).Offset.X, 0, .5);
        }, width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDeleteDoesNotRestoreAStrictCatalogSelection(bool deferred)
    {
        var commits = new List<string>();
        WithField(() =>
        {
            var box = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox { Text = LongName }, EditorFieldWidthRole.Reference);
            EditorAutoCompleteService.ConfigureStrict(box, [LongName, "Horse"], LongName,
                commitEmptyAsValid: true, deferSelectionCommit: deferred, valueCommitted: commits.Add);
            return box;
        }, async (window, owner, sink) =>
        {
            var box = Assert.IsType<AutoCompleteBox>(owner);
            Click(window, Input(box)); await Settle();
            Assert.Equal(LongName.Length, Math.Abs(Input(box).SelectionEnd - Input(box).SelectionStart));
            commits.Clear(); window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null); await Settle();
            Assert.Equal("", box.Text); Assert.True(box.IsKeyboardFocusWithin); Assert.Contains("", commits);
            sink.Focus(); await Settle(); Assert.Equal("", box.Text); Assert.Equal("", commits.Last());
            box.SelectedItem = "Horse"; await Settle(); Assert.Equal("Horse", box.Text); Assert.Equal("Horse", commits.Last());
        });
    }

    [Fact]
    public void FilledCatalogFirstClickDoesNotChangeTheCommittedValueAndTypingStillFilters()
        => WithField(() =>
        {
            var box = Catalog(); box.Text = LongName; return box;
        }, async (window, owner, _) =>
        {
            var box = Assert.IsType<AutoCompleteBox>(owner); var changes = 0;
            box.TextChanged += (_, _) => changes++;
            Click(window, Input(box)); await Settle();
            Assert.Equal(LongName.Length, Math.Abs(Input(box).SelectionEnd - Input(box).SelectionStart));
            Assert.Equal(LongName, box.Text); Assert.Equal(0, changes);
            window.KeyTextInput("Custom"); await Settle();
            var popup = box.GetTemplateDescendants().OfType<Popup>().Single();
            var list = popup.Child!.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.Contains("CustomUnit", list.Items.Cast<object>());
            Assert.DoesNotContain("Horse", list.Items.Cast<object>());
            // From a closed suggestion list, the first Down opens it; the next
            // Down navigates to a candidate. Keep the native keyboard sequence.
            if (!box.IsDropDownOpen)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                await Settle(); Assert.True(box.IsDropDownOpen);
            }
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            await Settle();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Settle(); Assert.Equal("CustomUnit", box.Text);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingSelectAllKeepsCaretAfterTheTypedLetterIncludingAnExactCatalogMatch(bool strict)
        => WithField(() =>
        {
            var box = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox(), EditorFieldWidthRole.Reference);
            if (strict) EditorAutoCompleteService.ConfigureStrict(box, ["A", "Apple", LongName], LongName, deferSelectionCommit: true);
            else { box.ItemsSource = new[] { "A", "Apple", LongName }; box.Text = LongName; }
            box.Text = LongName;
            return box;
        }, async (window, owner, _) =>
        {
            var input = Input(owner);
            Click(window, input); await Settle();
            Assert.Equal(LongName.Length, Math.Abs(input.SelectionEnd - input.SelectionStart));
            window.KeyTextInput("A"); await Settle();
            Assert.Equal("A", input.Text); Assert.Equal(1, input.CaretIndex);
            window.KeyTextInput("p"); await Settle();
            Assert.Equal("Ap", input.Text); Assert.Equal(2, input.CaretIndex);
        });

    [Fact]
    public void DisabledOriginalFieldExposesItsClippedLiveValueWithoutShowingATemplateButton()
        => WithField(() => { var field = TextField(); field.IsEnabled = false; return field; }, async (_, owner, _) =>
        {
            await Settle();
            Assert.True(ToolTip.GetShowOnDisabled(owner));
            Assert.False(OpenTip(owner).Cancel); Assert.Equal(LongName, ValueTip(owner).Text);
            Assert.DoesNotContain(owner.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible);
        });

    [Fact]
    public void ThirtyPixelLargeFontIdentityDoesNotCreateATrailingVerticalScrollbar()
        => WithField(() =>
        {
            var field = TextField(); field.FontSize = 16; field.FontWeight = Avalonia.Media.FontWeight.SemiBold;
            field.Height = 30; field.MinHeight = 30; field.MaxHeight = 30; field.IsEnabled = false;
            return field;
        }, async (_, owner, _) =>
        {
            await Settle();
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(owner));
            Assert.DoesNotContain(owner.GetVisualDescendants().OfType<ScrollBar>(), bar =>
                bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsEffectivelyVisible);
            Assert.Equal(LongName, Input(owner).Text);
        });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReadonlyCommandFieldsCanBeHitForClippedValueHoverWithoutEditing(bool selector)
        => WithField(() => selector
            ? ProtoUnitCommandFieldFactory.CreateStrictSelector(LongName, [LongName], enabled: false)
            : ProtoUnitCommandFieldFactory.CreateTextBox(LongName, EditorFieldWidthRole.Reference, enabled: false), async (window, owner, _) =>
        {
            await Settle();
            Assert.False(owner.IsEnabled); Assert.False(owner.Focusable);
            Assert.True(owner.IsHitTestVisible);
            // Tooltips opt into disabled controls. The default input query only
            // returns enabled controls, so use its explicit disabled-aware path.
            var hit = window.InputHitTest(At(window, owner), enabledElementsOnly: false) as Visual;
            Assert.NotNull(hit);
            Assert.True(ReferenceEquals(hit, owner) || hit.GetVisualAncestors().Contains(owner), "Read-only field is skipped by pointer hit-testing, so native hover cannot open its tooltip.");
            Assert.True(ToolTip.GetShowOnDisabled(owner));
            Assert.False(OpenTip(owner).Cancel); Assert.Equal(LongName, ValueTip(owner).Text);
            Click(window, owner); window.KeyTextInput("Changed"); await Settle();
            Assert.Equal(LongName, Input(owner).Text);
        });

    [Fact]
    public void StartVisibilityDoesNotFightTypingButReturnsOnBlurAndRecordLoad()
        => WithField(() => TextField(), async (window, owner, sink) =>
        {
            var input = Input(owner);
            Click(window, input); await Settle();
            input.SelectionStart = input.SelectionEnd = LongName.Length; input.CaretIndex = LongName.Length;
            window.KeyTextInput("Tail"); await Settle();
            Assert.True(Scroll(input).Offset.X > 0); Assert.EndsWith("Tail", input.Text);
            sink.Focus(); await Settle(); Assert.InRange(Scroll(input).Offset.X, 0, .5);
            input.Text = "NewRecord" + LongName; await Settle();
            Assert.InRange(Scroll(input).Offset.X, 0, .5);
        });

    [Theory]
    [InlineData("rollover")]
    [InlineData("multiline")]
    [InlineData("readonly")]
    public void RolloverMultilineAndReadOnlyFieldsDoNotGetFirstClickSelectAll(string kind)
        => WithField(() =>
        {
            var box = TextField(kind == "rollover" ? EditorFieldWidthRole.ShortRollover : EditorFieldWidthRole.SingleLineText);
            box.IsReadOnly = kind == "readonly";
            if (kind == "multiline") { box.AcceptsReturn = true; box.TextWrapping = Avalonia.Media.TextWrapping.Wrap; }
            return box;
        }, async (window, owner, _) =>
        {
            Click(window, Input(owner), .2); await Settle();
            Assert.NotEqual(LongName.Length, Math.Abs(Input(owner).SelectionEnd - Input(owner).SelectionStart));
            Assert.Equal(LongName, Input(owner).Text);
        });

    [Fact]
    public void TooltipMeasuresClippingReadsTheLatestValueAndPreservesExistingHelp()
        => WithField(() => TextField(), async (_, owner, _) =>
        {
            var input = Input(owner);
            Assert.False(OpenTip(owner).Cancel); Assert.Equal(LongName, ValueTip(owner).Text); Assert.True(ValueTip(owner).IsVisible);
            input.Text = "Short"; await Settle();
            Assert.True(OpenTip(owner).Cancel); Assert.False(ValueTip(owner).IsVisible);
            ToolTip.SetTip(owner, "Existing validation help");
            Assert.False(OpenTip(owner).Cancel);
            var tip = Assert.IsType<ScrollViewer>(ToolTip.GetTip(owner));
            Assert.Equal("Existing validation help", Assert.IsType<ContentPresenter>(Assert.IsType<StackPanel>(tip.Content).Children[1]).Content);
            input.Text = "Latest" + LongName; await Settle();
            Assert.False(OpenTip(owner).Cancel); Assert.Equal(input.Text, ValueTip(owner).Text);
            Assert.Equal(150, input.Width);
        });

    [Fact]
    public void AssetEntrySelectsTheFullPathAndDoesNotInvokeItsChangeCallback()
    {
        var calls = 0;
        const string path = "resources\\deep\\folder\\a_very_long_custom_icon.png";
        WithField(() =>
        {
            var asset = new AssetPathEditor();
            asset.Configure(path, [path, "resources\\other.png"], _ => { calls++; return Task.CompletedTask; });
            return EditorFormLayout.ApplyWidth(asset, EditorFieldWidthRole.AssetPath);
        }, async (window, owner, sink) =>
        {
            var asset = Assert.IsType<AssetPathEditor>(owner);
            Assert.False(OpenTip(asset).Cancel); Assert.Equal(path, ValueTip(asset).Text);
            Click(window, asset.CompactPresenter); await Settle();
            Assert.True(asset.Editor.IsVisible);
            Assert.True(asset.Editor.IsKeyboardFocusWithin);
            var input = Input(asset.Editor);
            Assert.Equal(path.Length, Math.Abs(input.SelectionEnd - input.SelectionStart));
            Assert.Equal(path, input.Text); Assert.InRange(Scroll(input).Offset.X, 0, .5);
            Assert.Equal(0, calls);
            // Closing the suggestion list must not close the focused text editor.
            // Once focus then leaves the field, it returns to compact mode.
            asset.Editor.IsDropDownOpen = false;
            Assert.True(asset.Editor.IsKeyboardFocusWithin);
            sink.Focus(); await Settle();
            Assert.False(asset.Editor.IsVisible);
            Assert.True(asset.CompactPresenter.IsVisible);
            Assert.Equal(path, asset.FullValue); Assert.Equal(0, calls);
        });
    }

    [Fact]
    public void DeferredSelectionIsRejectedAfterTextReplacementAndDoesNotStealNewFocus()
        => WithField(() => TextField(), async (window, owner, sink) =>
        {
            var input = Input(owner);
            Click(window, input);
            input.Text = "AnotherRecord";
            Click(window, sink); await Settle();
            Assert.True(sink.IsFocused);
            Assert.Equal("AnotherRecord", input.Text);
            Assert.Equal(input.SelectionStart, input.SelectionEnd);
            Click(window, input);
            Assert.IsType<StackPanel>(owner.Parent).Children.Remove(owner);
            await Settle(); Assert.False(owner.IsKeyboardFocusWithin);
        });

    [Fact]
    public void CatalogRefreshPreservesInputAndRejectsOldPresentationWork()
        => WithField(() =>
        {
            var options = new SelectorCatalog([LongName, "CustomUnit"]);
            var box = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox(), EditorFieldWidthRole.Reference);
            EditorAutoCompleteService.ConfigureStrict(box, options, LongName, deferSelectionCommit: true);
            return box;
        }, async (window, owner, _) =>
        {
            var box = Assert.IsType<AutoCompleteBox>(owner);
            Click(window, Input(box));
            box.Text = "NewDraft";
            EditorAutoCompleteService.RefreshCatalogSuggestions(box);
            Input(box).CaretIndex = 3; Input(box).SelectionStart = Input(box).SelectionEnd = 3;
            await Settle();
            Assert.Equal("NewDraft", box.Text);
            Assert.Equal(3, Input(box).CaretIndex); Assert.Equal(3, Input(box).SelectionEnd);
        });

    [Fact]
    public void ShortEnumPopupsFitTheirChoicesAndReopenWithUpdatedChoicesWithinTheCap()
        => WithField(() => EditorFormLayout.ApplyWidth(new ComboBox
        {
            ItemsSource = new[] { "Hack", "Divine" }, SelectedIndex = 0
        }, EditorFieldWidthRole.ShortEnum), async (window, owner, _) =>
        {
            var box = Assert.IsType<ComboBox>(owner);
            box.IsDropDownOpen = true; await Settle();
            var popup = box.GetTemplateDescendants().OfType<Popup>().Single();
            Assert.InRange(popup.Child!.Bounds.Width, 100, 160);
            Assert.Equal("Hack", box.SelectedItem);
            box.IsDropDownOpen = false; await Settle();
            box.ItemsSource = new[] { LongName, "Hack" }; box.SelectedItem = "Hack";
            box.IsDropDownOpen = true; await Settle();
            Assert.True(popup.Child.Bounds.Width is >= 319.5 and <= 320.5,
                $"popup {popup.Width}/{popup.MinWidth}/{popup.MaxWidth}; child {popup.Child.Bounds.Width}/{popup.Child.MinWidth}/{popup.Child.MaxWidth}");
            Assert.Equal("Hack", box.SelectedItem);
        });

    [Fact]
    public void ShortEnumAutocompleteUsesCompactPopupWithoutChangingCatalogSelection()
        => WithField(() =>
        {
            var box = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox(), EditorFieldWidthRole.ShortEnum);
            EditorAutoCompleteService.ConfigureStrict(box, ["Blood", "Cocoon", "Normal"], "", deferSelectionCommit: true);
            EditorAutoCompleteService.EnableDropdown(box);
            return box;
        }, async (window, owner, _) =>
        {
            var box = Assert.IsType<AutoCompleteBox>(owner);
            Click(window, Input(box)); await Settle();
            Assert.True(box.IsDropDownOpen);
            var popup = box.GetTemplateDescendants().OfType<Popup>().Single();
            Assert.InRange(popup.Child!.Bounds.Width, 100, 160);
            var list = popup.Child.GetLogicalDescendants().OfType<ListBox>().Single();
            Assert.Equal(3, list.ItemCount);
            box.SelectedItem = "Cocoon"; await Settle();
            Assert.Equal("Cocoon", box.Text);
        });

    [Fact]
    public void ClosedEnumKeepsNativeSelectionAndExistingVisualHelp()
        => WithField(() =>
        {
            var box = new ComboBox { ItemsSource = new[] { LongName, "Short" }, SelectedIndex = 0 };
            ToolTip.SetTip(box, new TextBlock { Text = "Existing help" });
            return EditorFormLayout.ApplyWidth(box, EditorFieldWidthRole.ShortEnum);
        }, async (window, owner, _) =>
        {
            var box = Assert.IsType<ComboBox>(owner);
            Assert.False(box.IsEditable);
            Assert.False(OpenTip(box).Cancel); Assert.True(ValueTip(box).IsVisible);
            Click(window, box); await Settle(); Assert.True(box.IsDropDownOpen);
            box.SelectedIndex = 1; box.IsDropDownOpen = false; await Settle();
            Assert.Equal("Short", box.SelectedItem); Assert.False(OpenTip(box).Cancel);
            Assert.False(ValueTip(box).IsVisible);
        });

    [Theory]
    [InlineData("busy")]
    [InlineData("disabled")]
    [InlineData("reattached")]
    public void QueuedSelectionCannotOverrideBusyDisabledOrReattachedInput(string transition)
    {
        var busy = false;
        WithField(() =>
        {
            var box = TextField();
            EditorFieldInteraction.Configure(box, EditorFieldWidthRole.CompactReference, () => busy);
            return box;
        }, async (window, owner, _) =>
        {
            var input = Input(owner); var changes = 0;
            input.TextChanged += (_, _) => changes++;
            Click(window, input);
            if (transition == "busy") busy = true;
            else if (transition == "disabled") input.IsEnabled = false;
            else
            {
                var parent = Assert.IsType<StackPanel>(owner.Parent);
                parent.Children.Remove(owner); parent.Children.Add(owner); input.Focus();
            }
            input.CaretIndex = input.SelectionStart = input.SelectionEnd = 3;
            await Settle();
            Assert.Equal(3, input.CaretIndex); Assert.Equal(3, input.SelectionEnd);
            Assert.Equal(LongName, input.Text); Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void EditableClosedCatalogKeepsItsOptionsCommitRulesAndEntrySelection()
        => WithField(() => EditorFormLayout.ApplyWidth(
            new StrictSearchableComboBox([LongName, "CustomUnit"], LongName), EditorFieldWidthRole.Reference),
            async (window, owner, sink) =>
            {
                var box = Assert.IsType<StrictSearchableComboBox>(owner); var commits = 0;
                box.ValueCommitted += (_, _) => commits++;
                Click(window, Input(box)); await Settle();
                Assert.Equal(LongName.Length, Math.Abs(Input(box).SelectionEnd - Input(box).SelectionStart));
                Assert.InRange(Scroll(Input(box)).Offset.X, 0, .5);
                Assert.Equal(LongName, box.Value); Assert.Equal(0, commits);
                window.KeyTextInput("CustomUnit"); await Settle();
                sink.Focus(); await Settle();
                Assert.Equal("CustomUnit", box.Value); Assert.Contains(LongName, box.Options);
                Assert.True(commits > 0);
                Input(box).Focus();
                box.SelectedItem = LongName; await Settle();
                Assert.Equal(LongName, box.Value);
                Assert.InRange(Scroll(Input(box)).Offset.X, 0, .5);
            });
}
