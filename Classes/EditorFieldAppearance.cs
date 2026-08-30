using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Shared field appearance used by the mature ProtoUnit/Ability/Tactics editors and
/// standalone editors that need to visually match them.
/// </summary>
public static class EditorFieldAppearance
{
    public static void ApplyStandard(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.Background = Brush.Parse("#0E1110");
                textBox.Foreground = Brush.Parse("#E8DECC");
                textBox.BorderBrush = Brush.Parse("#4C4031");
                textBox.BorderThickness = new Thickness(1);
                break;
            case AutoCompleteBox autoCompleteBox:
                autoCompleteBox.Background = Brush.Parse("#0E1110");
                autoCompleteBox.Foreground = Brush.Parse("#E8DECC");
                autoCompleteBox.BorderBrush = Brush.Parse("#4C4031");
                autoCompleteBox.BorderThickness = new Thickness(1);
                break;
        }
    }


    public static void ApplyReadOnly(Control control)
    {
        control.IsEnabled = false;
        control.IsHitTestVisible = false;
        control.Focusable = false;
        if (control is TextBox textBox)
            textBox.IsReadOnly = true;
    }

    public static TextBox CreateReadOnlyTextBox(string value, double width = 240)
    {
        return new TextBox
        {
            Text = value,
            IsEnabled = false,
            IsReadOnly = true,
            IsHitTestVisible = false,
            Focusable = false,
            Width = width,
            MaxWidth = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
    }
}
