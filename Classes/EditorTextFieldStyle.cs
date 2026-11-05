using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Classes;

public static class EditorTextFieldStyle
{
    // Compatibility default for unmigrated callers. New fields choose an explicit
    // role; do not globally resize existing editors during the staged migration.
    public const double StandardWidth = 380;

    public static TextBox ConfigureTextBox(TextBox textBox)
    {
        if (EditorRolloverField.IsLong(textBox)) return EditorRolloverField.Configure(textBox);
        EditorFormLayout.ApplyConfiguredWidth(textBox, StandardWidth);
        textBox.HorizontalAlignment = HorizontalAlignment.Left;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        return textBox;
    }

    public static TextBox ConfigureTextBox(TextBox textBox, EditorFieldWidthRole role)
    {
        EditorFormLayout.ApplyWidth(textBox, role);
        return ConfigureTextBox(textBox);
    }

    public static AutoCompleteBox ConfigureSelector(AutoCompleteBox selector)
    {
        EditorFormLayout.ApplyConfiguredWidth(selector, StandardWidth);
        selector.HorizontalAlignment = HorizontalAlignment.Left;
        return selector;
    }

    public static AutoCompleteBox ConfigureSelector(AutoCompleteBox selector, EditorFieldWidthRole role)
    {
        EditorFormLayout.ApplyWidth(selector, role);
        return ConfigureSelector(selector);
    }
}
