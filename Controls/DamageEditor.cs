using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared visual row for a damage type and amount. Damage catalog filtering,
/// inheritance markers, validation, and XML ownership remain with the host.
/// </summary>
public sealed class DamageEditor : Grid
{
    public ComboBox TypeField { get; }
    public TextBox ValueField { get; }
    public ContentControl SourceMarkerHost { get; } = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    public Button? RemoveButton { get; }

    public DamageEditor(ComboBox typeField, TextBox valueField, bool showRemoveButton)
    {
        TypeField = typeField;
        ValueField = valueField;
        EditorFormLayout.ApplyWidth(typeField, EditorFieldWidthRole.ShortEnum);
        EditorFormLayout.ApplyWidth(valueField, EditorFieldWidthRole.Numeric);

        ColumnDefinitions = showRemoveButton
            ? new ColumnDefinitions("Auto, Auto, Auto, Auto")
            : new ColumnDefinitions("Auto, Auto, Auto");
        Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        TypeField.Margin = new Thickness(0, 0, 4, 0);
        TypeField.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(TypeField);

        ValueField.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ValueField, 1);
        Children.Add(ValueField);

        Grid.SetColumn(SourceMarkerHost, 2);
        Children.Add(SourceMarkerHost);

        if (showRemoveButton)
        {
            RemoveButton = EditorRemoveButtons.Row(description: "Remove damage entry");
            Grid.SetColumn(RemoveButton, 3);
            Children.Add(RemoveButton);
        }
    }

    public void SetSourceMarker(Control? marker)
        => SourceMarkerHost.Content = marker;
}
