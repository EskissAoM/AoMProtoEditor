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

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = "Targets:",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(ModeField);
        SimpleTypeLabel = new TextBlock
        {
            Text = "Type:",
            VerticalAlignment = VerticalAlignment.Center
        };
        headerRow.Children.Add(SimpleTypeLabel);
        SimpleTargetField.Margin = new Thickness(0);
        headerRow.Children.Add(SimpleTargetField);
        Children.Add(headerRow);
        Children.Add(MultipleTargetsField);

        ModeField.SelectionChanged += (_, _) => RefreshModeVisibility();
        RefreshModeVisibility();
    }

    public void RefreshModeVisibility()
    {
        SimpleTypeLabel.IsVisible = !IsMultiple;
        SimpleTargetField.IsVisible = !IsMultiple;
        MultipleTargetsField.IsVisible = IsMultiple;
    }
}
