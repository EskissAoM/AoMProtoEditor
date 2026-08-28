using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared visual row for a typed rate. The host retains rate semantics,
/// catalogue filtering, tactics inheritance, validation, and XML serialization.
/// </summary>
public sealed class RateEditor : Grid
{
    public Control TypeField { get; }
    public Control ValueField { get; }
    public Button? RemoveButton { get; }

    public RateEditor(
        Control typeField,
        Control valueField,
        string label = "Type:",
        bool showRemoveButton = true)
    {
        TypeField = typeField;
        ValueField = valueField;

        ColumnDefinitions = showRemoveButton
            ? new ColumnDefinitions("Auto, Auto, Auto, Auto")
            : new ColumnDefinitions("Auto, Auto, Auto");
        Margin = new Thickness(0, 0, 8, 4);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(labelBlock);

        TypeField.Margin = new Thickness(0, 0, 4, 0);
        TypeField.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(TypeField, 1);
        Children.Add(TypeField);

        ValueField.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ValueField, 2);
        Children.Add(ValueField);

        if (showRemoveButton)
        {
            RemoveButton = new Button
            {
                Classes = { "remove-button" },
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(RemoveButton, 3);
            Children.Add(RemoveButton);
        }
    }
}
