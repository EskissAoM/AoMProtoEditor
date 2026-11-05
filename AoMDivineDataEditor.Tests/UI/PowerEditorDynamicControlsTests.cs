using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using AoMDivineDataEditor.Controls;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerEditorDynamicControlsTests
{
    [Fact]
    public void CardsAndButtonsPreservePowerVisualContracts()
    {
        AvaloniaTestHost.Run(() =>
        {
            var (container, body) = PowerEditorDynamicControls.CreateCard();
            var optionalTag = new object();
            var removeTag = new object();
            var optional = PowerEditorDynamicControls.CreateOptionalButton("Optional", optionalTag);
            var remove = PowerEditorDynamicControls.CreateRemoveButton(removeTag);

            Assert.Contains("temp-unit-card", container.Classes);
            Assert.Same(body, container.Child);
            Assert.Equal(4, body.Spacing);
            Assert.Equal("Optional", optional.Content);
            Assert.Same(optionalTag, optional.Tag);
            Assert.Contains("add-component", optional.Classes);
            Assert.Same(removeTag, remove.Tag);
            Assert.Contains("remove-button", remove.Classes);

            // Shared field removal supplies its actual gap; owning cards may
            // still set their own placement outside the field group.
            Assert.Equal(default, optional.Margin);
            Assert.Equal(4, remove.Margin.Left);
        });
    }

    [Fact]
    public void LabeledSegmentsPreserveControlOrderAndUseSharedSpacing()
    {
        AvaloniaTestHost.Run(() =>
        {
            var control = new TextBox();
            var segment = PowerEditorDynamicControls.CreateLabeledSegment("Amount", control);
            var label = Assert.IsType<TextBlock>(segment.Children[0]);

            Assert.Equal(Avalonia.Layout.Orientation.Horizontal, segment.Orientation);
            Assert.Equal("Amount", label.Text);
            Assert.Equal(8, label.Margin.Right);
            Assert.Equal(0, label.Margin.Bottom);
            Assert.Same(control, segment.Children[1]);

            var unlabeledControl = new TextBox();
            var unlabeled = PowerEditorDynamicControls.CreateLabeledSegment("", unlabeledControl);
            Assert.Same(unlabeledControl, Assert.Single(unlabeled.Children));
        });
    }

    [Fact]
    public void StrictSelectorsPreserveUnknownValuesGeometryAndCommitGuards()
    {
        AvaloniaTestHost.Run(() =>
        {
            var busy = false;
            var canCommit = true;
            var committed = new List<string>();
            var owner = new ScrollViewer();
            var selector = PowerEditorDynamicControls.CreateStrictSelector(
                ["Known", "known", "Other"], "ModdedValue", true,
                () => busy, () => canCommit, committed.Add, "field", owner, EditorFieldWidthRole.CompactReference);
            var values = Assert.IsAssignableFrom<IEnumerable<string>>(selector.ItemsSource).ToList();

            Assert.Equal(150, selector.Width);
            Assert.Equal(150, selector.MinWidth);
            Assert.Equal(150, selector.MaxWidth);
            Assert.True(EditorFieldInteraction.IsConfigured(selector));
            Assert.Equal(AutoCompleteFilterMode.Contains, selector.FilterMode);
            Assert.Equal(0, selector.MinimumPrefixLength);
            Assert.Equal(14, selector.Margin.Right);
            Assert.Equal(4, selector.Margin.Bottom);
            Assert.Equal("field", selector.Tag);
            Assert.Equal("ModdedValue", selector.Text);
            Assert.Equal(3, values.Count);
            Assert.Contains("Known", values);
            Assert.Contains("Other", values);
            Assert.Contains("ModdedValue", values);
            Assert.Single(values, value => value.Equals("Known", StringComparison.OrdinalIgnoreCase));

            // Strict-selector initialization may report its preserved initial value.
            committed.Clear();
            busy = true;
            selector.SelectedItem = "Known";
            Assert.Empty(committed);

            busy = false;
            canCommit = false;
            selector.SelectedItem = "Other";
            Assert.Empty(committed);
        });
    }

    [Fact]
    public void ChipsRetainEditableAndReadOnlyRemovalBehavior()
    {
        AvaloniaTestHost.Run(() =>
        {
            var removals = 0;
            var editable = PowerEditorDynamicControls.CreateChip("Unit", () => removals++);
            var editableBody = Assert.IsType<StackPanel>(editable.Child);
            var remove = Assert.Single(editableBody.Children.OfType<Button>());
            Assert.Contains("chip-remove-button", remove.Classes);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, removals);

            var readOnly = PowerEditorDynamicControls.CreateChip("Unit", () => removals++, readOnly: true);
            var readOnlyBody = Assert.IsType<StackPanel>(readOnly.Child);
            Assert.Empty(readOnlyBody.Children.OfType<Button>());
        });
    }
}
