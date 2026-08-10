using Avalonia.Controls;
using Avalonia.Layout;

namespace CryBarEditor.Classes;

/// <summary>
/// Shared ProtoUnit Command field constructors used by both the standalone command editor
/// and the inline Transform Command editor in the ProtoUnit Commands tab.
/// </summary>
public static class ProtoUnitCommandFieldFactory
{
    public static TextBox CreateTextBox(string value, double width = 240, bool enabled = true)
    {
        var editor = new TextBox
        {
            Text = value ?? string.Empty,
            Width = width,
            MaxWidth = width,
            IsEnabled = enabled,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (enabled)
            EditorFieldAppearance.ApplyStandard(editor);
        else
            EditorFieldAppearance.ApplyReadOnly(editor);
        return editor;
    }

    public static AutoCompleteBox CreateStrictSelector(
        string value,
        IEnumerable<string> suggestions,
        bool enabled = true,
        double width = 240,
        Action<string>? valueCommitted = null,
        Func<bool>? isBusy = null)
    {
        var editor = new AutoCompleteBox
        {
            Text = value ?? string.Empty,
            Width = width,
            MaxWidth = width,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = enabled,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        EditorTextFieldStyle.ConfigureSelector(editor);
        // ConfigureSelector applies the global 50-character width. Shared command
        // layouts sometimes intentionally request a narrower fixed width, so restore
        // the caller's width after applying the common selector behavior.
        editor.Width = width;
        editor.MaxWidth = width;
        if (enabled)
            EditorFieldAppearance.ApplyStandard(editor);
        else
            EditorFieldAppearance.ApplyReadOnly(editor);
        EditorAutoCompleteService.ConfigureStrict(
            editor,
            suggestions,
            value ?? string.Empty,
            isBusy,
            preserveUnknownInitialValue: true,
            allowEmpty: true,
            commitEmptyAsValid: true,
            deferSelectionCommit: true,
            valueCommitted: valueCommitted);
        return editor;
    }
}
