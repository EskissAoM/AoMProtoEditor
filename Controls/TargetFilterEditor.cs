using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared presentation for Simple/Multiple target selection. The host owns the
/// accepted catalog, chips, validation, inheritance, and XML tag mapping.
/// </summary>
public sealed class TargetFilterEditor : StackPanel
{
    public ComboBox ModeField { get; }
    public Control SimpleTargetField { get; }
    public Control MultipleTargetsField { get; }
    public TextBlock SimpleTypeLabel { get; }

    public bool IsMultiple => string.Equals(
        ModeField.SelectedItem?.ToString() ?? ModeField.SelectedValue?.ToString(),
        "Multiple",
        StringComparison.OrdinalIgnoreCase);

    public TargetFilterEditor(
        ComboBox modeField,
        Control simpleTargetField,
        Control multipleTargetsField)
    {
        ModeField = modeField;
        SimpleTargetField = simpleTargetField;
        MultipleTargetsField = multipleTargetsField;

        Spacing = 4;
        Margin = new Thickness(0, 2, 0, 0);

        var headerRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var modeLabel = new TextBlock
        {
            Text = "Targets:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 8), Children = { modeLabel, ModeField } });
        SimpleTypeLabel = new TextBlock
        {
            Text = "Type:",
            VerticalAlignment = VerticalAlignment.Center
        };
        SimpleTargetField.Margin = new Thickness(0);
        var simpleGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 8), Children = { SimpleTypeLabel, SimpleTargetField } };
        headerRow.Children.Add(simpleGroup);
        Children.Add(headerRow);
        Children.Add(MultipleTargetsField);

        ModeField.SelectionChanged += (_, _) => RefreshModeVisibility();
        RefreshModeVisibility();
    }

    public void RefreshModeVisibility()
    {
        SimpleTypeLabel.IsVisible = !IsMultiple;
        SimpleTargetField.IsVisible = !IsMultiple;
        if (SimpleTypeLabel.Parent is Control group) group.IsVisible = !IsMultiple;
        MultipleTargetsField.IsVisible = IsMultiple;
    }

    /// <summary>
    /// Builds one labeled multiple-target chip group. The label is part of the
    /// read-only presentation contract; only the picker is omitted in read-only mode.
    /// </summary>
    public static StackPanel CreateChipGroup(
        string title,
        AutoCompleteBox picker,
        WrapPanel chips,
        bool isReadOnly)
    {
        var host = new StackPanel { Spacing = 8 };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new TextBlock
        {
            Text = title + ":",
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!isReadOnly)
            header.Children.Add(picker);
        host.Children.Add(header);
        host.Children.Add(chips);
        return host;
    }
}
