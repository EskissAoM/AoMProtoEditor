using Avalonia.Controls;
using Avalonia.Interactivity;
using AoMDivineDataEditor.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class OptionalFieldHostTests
{
    [Fact]
    public void SetExpanded_UsesTheStandardOptionalFieldLayout()
    {
        AvaloniaTestHost.Run(() =>
        {
            var editor = new TextBox();
            var host = CreateHost(editor, isExpanded: false);

            Assert.False(host.IsExpanded);
            Assert.Same(host.AddButton, Assert.Single(host.Children));

            host.SetExpanded(true);

            Assert.True(host.IsExpanded);
            Assert.Equal(3, host.Children.Count);
            Assert.Same(editor, host.Children[1]);
            Assert.Equal(10, ((TextBlock)host.Children[0]).Margin.Right);
            Assert.Equal(2, host.RemoveButton.Margin.Left);
        });
    }

    [Fact]
    public void AddAndRemove_ChangePresentationOnlyAfterTheCallerAcceptsTheTransition()
    {
        AvaloniaTestHost.Run(() =>
        {
            var allowAdd = false;
            var allowRemove = false;
            var host = new OptionalFieldHost(
                "Optional:",
                new TextBox(),
                "Add Optional",
                isExpanded: false,
                isReadOnly: false,
                () => Task.FromResult(allowAdd),
                () => Task.FromResult(allowRemove));

            host.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(host.IsExpanded);

            allowAdd = true;
            host.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(host.IsExpanded);

            host.RemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(host.IsExpanded);

            allowRemove = true;
            host.RemoveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(host.IsExpanded);
        });
    }

    [Fact]
    public void ReadOnlyHost_KeepsExistingValuesVisibleAndPreventsAddingMissingOnes()
    {
        AvaloniaTestHost.Run(() =>
        {
            var collapsed = new OptionalFieldHost(
                "Optional:", new TextBox(), "Add Optional", false, true,
                () => Task.FromResult(true), () => Task.FromResult(true));
            var expanded = new OptionalFieldHost(
                "Optional:", new TextBox(), "Add Optional", true, true,
                () => Task.FromResult(true), () => Task.FromResult(true));

            Assert.False(collapsed.AddButton.IsEnabled);
            Assert.Empty(collapsed.Children);
            Assert.Equal(2, expanded.Children.Count);
            Assert.DoesNotContain(expanded.RemoveButton, expanded.Children);
        });
    }

    [Fact]
    public void AttachmentEditor_StandardizesTheSharedPathAndBonePair()
    {
        AvaloniaTestHost.Run(() =>
        {
            var attachment = new TextBox();
            var bone = new TextBox();
            var editor = new AttachmentEditor(
                attachment,
                bone,
                "Target Attachment:",
                "Bone:");

            Assert.Equal(AttachmentEditor.AttachmentWidth, attachment.Width);
            Assert.Equal(AttachmentEditor.BoneWidth, bone.Width);
            Assert.Equal(2, editor.Children.Count);
            Assert.Equal(
                "Target Attachment:",
                ((TextBlock)((StackPanel)editor.Children[0]).Children[0]).Text);
            Assert.Equal(
                "Bone:",
                ((TextBlock)((StackPanel)editor.Children[1]).Children[0]).Text);
        });
    }

    [Fact]
    public void RateEditor_UsesStandardLabelAndRemoveSpacing()
    {
        AvaloniaTestHost.Run(() =>
        {
            var type = new ComboBox();
            var value = new TextBox();
            var editor = new RateEditor(type, value, "Resource:");

            Assert.Equal("Resource:", ((TextBlock)editor.Children[0]).Text);
            Assert.Same(type, editor.Children[1]);
            Assert.Same(value, editor.Children[2]);
            Assert.Same(editor.RemoveButton, editor.Children[3]);
            Assert.Equal(8, ((TextBlock)editor.Children[0]).Margin.Right);
            Assert.Equal(2, editor.RemoveButton!.Margin.Left);
        });
    }

    [Fact]
    public void ModifierEditor_CollapsesDamageTypeAsOneCompleteGroup()
    {
        AvaloniaTestHost.Run(() =>
        {
            var modify = new AutoCompleteBox();
            var damageType = new ComboBox();
            var value = new TextBox();
            var editor = new ModifierEditor(modify, value, damageType);

            Assert.Same(modify, editor.ModifyTypeField);
            Assert.Same(damageType, editor.DamageTypeField);
            Assert.Same(value, editor.ValueField);
            Assert.NotNull(editor.RemoveButton);
            Assert.True(editor.DamageTypeGroup!.IsVisible);

            editor.SetDamageTypeVisible(false);

            Assert.False(editor.DamageTypeGroup.IsVisible);
            Assert.Equal(2, editor.RemoveButton.Margin.Left);
        });
    }

    [Fact]
    public void DamageEditor_KeepsSourceMarkerBetweenValueAndRemoveButton()
    {
        AvaloniaTestHost.Run(() =>
        {
            var type = new ComboBox();
            var value = new TextBox();
            var marker = new Border();
            var editor = new DamageEditor(type, value, showRemoveButton: true);

            editor.SetSourceMarker(marker);

            Assert.Same(type, editor.Children[0]);
            Assert.Same(value, editor.Children[1]);
            Assert.Same(marker, editor.SourceMarkerHost.Content);
            Assert.Same(editor.RemoveButton, editor.Children[3]);
            Assert.Equal(2, editor.RemoveButton!.Margin.Left);
        });
    }

    [Fact]
    public void TargetFilterEditor_SwitchesCompleteSimpleAndMultipleGroups()
    {
        AvaloniaTestHost.Run(() =>
        {
            var mode = new ComboBox
            {
                ItemsSource = new[] { "Simple", "Multiple" },
                SelectedItem = "Simple"
            };
            var simple = new AutoCompleteBox();
            var multiple = new Grid();
            var editor = new TargetFilterEditor(mode, simple, multiple);

            Assert.False(editor.IsMultiple);
            Assert.True(simple.IsVisible);
            Assert.False(multiple.IsVisible);

            mode.SelectedItem = "Multiple";
            editor.RefreshModeVisibility();

            Assert.True(editor.IsMultiple);
            Assert.False(simple.IsVisible);
            Assert.True(multiple.IsVisible);
        });
    }

    [Fact]
    public void TargetFilterChipGroup_KeepsItsLabelInReadOnlyMode()
    {
        AvaloniaTestHost.Run(() =>
        {
            var readOnlyPicker = new AutoCompleteBox();
            var readOnlyChips = new WrapPanel();
            var readOnly = TargetFilterEditor.CreateChipGroup(
                "Attack Type", readOnlyPicker, readOnlyChips, isReadOnly: true);
            var readOnlyHeader = Assert.IsType<StackPanel>(readOnly.Children[0]);

            Assert.Equal("Attack Type:", Assert.IsType<TextBlock>(readOnlyHeader.Children[0]).Text);
            Assert.DoesNotContain(readOnlyPicker, readOnlyHeader.Children);
            Assert.Same(readOnlyChips, readOnly.Children[1]);

            var editablePicker = new AutoCompleteBox();
            var editable = TargetFilterEditor.CreateChipGroup(
                "Ignore Type", editablePicker, new WrapPanel(), isReadOnly: false);
            var editableHeader = Assert.IsType<StackPanel>(editable.Children[0]);
            Assert.Equal("Ignore Type:", Assert.IsType<TextBlock>(editableHeader.Children[0]).Text);
            Assert.Contains(editablePicker, editableHeader.Children);
        });
    }

    [Fact]
    public void OnHitEffectEditor_OwnsTheSharedCardShellOnly()
    {
        AvaloniaTestHost.Run(() =>
        {
            var type = new AutoCompleteBox();
            var active = new CheckBox();
            var marker = new Border();
            var editor = new OnHitEffectEditor(
                type,
                active,
                isReadOnly: false,
                isSupported: true,
                marker);

            Assert.Same(type, editor.TypeField);
            Assert.Same(active, editor.ActiveField);
            Assert.Contains("editor-card", editor.CardSurface.Classes);
            Assert.IsType<Border>(editor.CardSurface);
            Assert.Same(editor.Header, editor.Body.Children[0]);
            Assert.Contains(type, editor.Header.Children);
            Assert.Contains(active, editor.Header.Children);
            Assert.Contains(marker, editor.Header.Children);
            Assert.NotNull(editor.RemoveButton);
            Assert.Contains(editor.RemoveButton!, editor.Header.Children);

            var window = new Window { Content = editor };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, editor.CardSurface.BorderThickness.Left);
            Assert.Equal(1, editor.CardSurface.BorderThickness.Top);
            window.Close();
        });
    }

    [Fact]
    public void OnHitEffectEditor_CanHideActiveWithoutRemovingTheCardSurface()
    {
        AvaloniaTestHost.Run(() =>
        {
            var active = new CheckBox();
            var editor = new OnHitEffectEditor(
                new AutoCompleteBox(),
                active,
                isReadOnly: false,
                isSupported: true,
                showActiveField: false);

            Assert.DoesNotContain(active, editor.Header.Children);
            Assert.DoesNotContain(editor.Header.Children.OfType<TextBlock>(), label => label.Text == "Active:");
            Assert.Contains("editor-card", editor.CardSurface.Classes);
            Assert.NotNull(editor.RemoveButton);
        });
    }

    [Fact]
    public void OnHitEffectEditor_ReadOnlyUnsupportedCardPreservesItsStatusWithoutRemoval()
    {
        AvaloniaTestHost.Run(() =>
        {
            var editor = new OnHitEffectEditor(
                new AutoCompleteBox(),
                new CheckBox(),
                isReadOnly: true,
                isSupported: false);

            Assert.Null(editor.RemoveButton);
            Assert.Contains(
                editor.Header.Children.OfType<TextBlock>(),
                label => label.Text == "Unsupported in editor for now; XML will be preserved.");
        });
    }

    [Fact]
    public void ChargedEditor_OnlyOffersContainerRemovalWhenTheHostAllowsIt()
    {
        AvaloniaTestHost.Run(() =>
        {
            var removable = new ChargedEditor(isReadOnly: false, showRemoveButton: true);
            var single = new ChargedEditor(isReadOnly: false, showRemoveButton: false);
            var readOnly = new ChargedEditor(isReadOnly: true, showRemoveButton: true);

            Assert.Same(removable.Body, removable.Root.Children[0]);
            Assert.NotNull(removable.RemoveButton);
            Assert.Contains(removable.RemoveButton!, removable.Root.Children);
            Assert.Null(single.RemoveButton);
            Assert.Null(readOnly.RemoveButton);
        });
    }

    private static OptionalFieldHost CreateHost(Control editor, bool isExpanded)
        => new(
            "Optional:", editor, "Add Optional", isExpanded, false,
            () => Task.FromResult(true), () => Task.FromResult(true));

}
