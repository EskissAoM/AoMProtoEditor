using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Classes;

public static class EditorTextFieldStyle
{
    // A stable width is preferable to measuring at each dynamic editor rebuild.
    // It is sized for roughly fifty normal UI characters in the Fluent TextBox.
    public const double StandardWidth = 380;

    public static TextBox ConfigureTextBox(TextBox textBox)
    {
        textBox.Width = StandardWidth;
        textBox.MaxWidth = StandardWidth;
        textBox.HorizontalAlignment = HorizontalAlignment.Left;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        return textBox;
    }

    public static AutoCompleteBox ConfigureSelector(AutoCompleteBox selector)
    {
        selector.Width = StandardWidth;
        selector.MaxWidth = StandardWidth;
        selector.HorizontalAlignment = HorizontalAlignment.Left;
        return selector;
    }
}
