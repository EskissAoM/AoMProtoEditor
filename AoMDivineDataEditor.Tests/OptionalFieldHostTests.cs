using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using AoMDivineDataEditor.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class OptionalFieldHostTests
{
    private static bool _avaloniaInitialized;

    [Fact]
    public void SetExpanded_UsesTheStandardOptionalFieldLayout()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void AddAndRemove_ChangePresentationOnlyAfterTheCallerAcceptsTheTransition()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void ReadOnlyHost_KeepsExistingValuesVisibleAndPreventsAddingMissingOnes()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void AttachmentEditor_StandardizesTheSharedPathAndBonePair()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void RateEditor_UsesStandardLabelAndRemoveSpacing()
    {
        EnsureAvalonia();
        var type = new ComboBox();
        var value = new TextBox();
        var editor = new RateEditor(type, value, "Resource:");

        Assert.Equal("Resource:", ((TextBlock)editor.Children[0]).Text);
        Assert.Same(type, editor.Children[1]);
        Assert.Same(value, editor.Children[2]);
        Assert.Same(editor.RemoveButton, editor.Children[3]);
        Assert.Equal(8, ((TextBlock)editor.Children[0]).Margin.Right);
        Assert.Equal(2, editor.RemoveButton!.Margin.Left);
    }

    [Fact]
    public void ModifierEditor_CollapsesDamageTypeAsOneCompleteGroup()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void DamageEditor_KeepsSourceMarkerBetweenValueAndRemoveButton()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void TargetFilterEditor_SwitchesCompleteSimpleAndMultipleGroups()
    {
        EnsureAvalonia();
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
    }

    [Fact]
    public void OnHitEffectEditor_OwnsTheSharedCardShellOnly()
    {
        EnsureAvalonia();
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
        Assert.Same(editor.Header, editor.Body.Children[0]);
        Assert.Contains(type, editor.Header.Children);
        Assert.Contains(active, editor.Header.Children);
        Assert.Contains(marker, editor.Header.Children);
        Assert.NotNull(editor.RemoveButton);
        Assert.Contains(editor.RemoveButton!, editor.Header.Children);
    }

    [Fact]
    public void OnHitEffectEditor_ReadOnlyUnsupportedCardPreservesItsStatusWithoutRemoval()
    {
        EnsureAvalonia();
        var editor = new OnHitEffectEditor(
            new AutoCompleteBox(),
            new CheckBox(),
            isReadOnly: true,
            isSupported: false);

        Assert.Null(editor.RemoveButton);
        Assert.Contains(
            editor.Header.Children.OfType<TextBlock>(),
            label => label.Text == "Unsupported in editor for now; XML will be preserved.");
    }

    [Fact]
    public void ChargedEditor_OnlyOffersContainerRemovalWhenTheHostAllowsIt()
    {
        EnsureAvalonia();
        var removable = new ChargedEditor(isReadOnly: false, showRemoveButton: true);
        var single = new ChargedEditor(isReadOnly: false, showRemoveButton: false);
        var readOnly = new ChargedEditor(isReadOnly: true, showRemoveButton: true);

        Assert.Same(removable.Body, removable.Root.Children[0]);
        Assert.NotNull(removable.RemoveButton);
        Assert.Contains(removable.RemoveButton!, removable.Root.Children);
        Assert.Null(single.RemoveButton);
        Assert.Null(readOnly.RemoveButton);
    }

    private static OptionalFieldHost CreateHost(Control editor, bool isExpanded)
        => new(
            "Optional:", editor, "Add Optional", isExpanded, false,
            () => Task.FromResult(true), () => Task.FromResult(true));

    private static void EnsureAvalonia()
    {
        if (_avaloniaInitialized || Application.Current != null)
        {
            _avaloniaInitialized = true;
            return;
        }

        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        _avaloniaInitialized = true;
    }
}
