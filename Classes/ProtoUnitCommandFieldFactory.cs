using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Shared ProtoUnit Command field constructors used by both the standalone command editor
/// and the inline Transform Command editor in the ProtoUnit Commands tab.
/// </summary>
public static class ProtoUnitCommandFieldFactory
{
    // Compatibility constructor for legacy fixed-width callers. Resolved long
    // rollover bodies use EditorRolloverField and an allocated parent column.
    public static TextBox CreateTextBox(string value, double width, bool enabled = true)
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

    public static TextBox CreateTextBox(string value, EditorFieldWidthRole role = EditorFieldWidthRole.SingleLineText,
        bool enabled = true, Func<bool>? isBusy = null)
    {
        var editor = ConfigureField(new TextBox { Text = value ?? string.Empty, IsEnabled = enabled }, role, isBusy);
        if (enabled) EditorFieldAppearance.ApplyStandard(editor);
        else EditorFieldAppearance.ApplyReadOnly(editor);
        return editor;
    }

    public static T ConfigureField<T>(T editor, EditorFieldWidthRole role, Func<bool>? isBusy = null) where T : Control
    {
        EditorFormLayout.ApplyWidth(editor, role);
        EditorFieldInteraction.Configure(editor, role, isBusy);
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        if (editor is AutoCompleteBox or ComboBox || editor is TextBox { AcceptsReturn: false })
            editor.MinHeight = editor.Height = editor.MaxHeight = EditorFormLayout.FieldHeight;
        if (editor is AutoCompleteBox selector) EditorAutoCompleteService.KeepDropDownAnchored(selector);
        return editor;
    }

    public static AutoCompleteBox CreateStrictSelector(
        string value,
        IEnumerable<string> suggestions,
        bool enabled = true,
        EditorFieldWidthRole role = EditorFieldWidthRole.Reference,
        Action<string>? valueCommitted = null,
        Func<bool>? isBusy = null)
    {
        var editor = new AutoCompleteBox
        {
            Text = value ?? string.Empty,
            FilterMode = AutoCompleteFilterMode.Contains,
            IsEnabled = enabled,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ConfigureField(editor, role, isBusy);
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
