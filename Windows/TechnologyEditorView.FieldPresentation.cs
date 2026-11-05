using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView
{
    private static void ObserveTechnologyTextEdits(TextBox field, EventHandler<TextChangedEventArgs> onChanged)
    {
        // Avalonia queues TextChanged for the initial Text assignment. Loading may
        // already be over when it arrives; only a different value is an edit.
        var previous = field.Text ?? "";
        field.TextChanged += (sender, args) =>
        {
            var current = field.Text ?? "";
            if (current == previous) return;
            previous = current;
            onChanged(sender, args);
        };
    }

    private T ConfigureTechnologyField<T>(T field, EditorFieldWidthRole role) where T : Control
    {
        EditorFormLayout.ApplyWidth(field, role);
        EditorFieldInteraction.Configure(field, role, () => _loadingUi);
        if (field is AutoCompleteBox or ComboBox || field is TextBox { AcceptsReturn: false })
        {
            field.MinHeight = field.Height = field.MaxHeight = EditorFormLayout.FieldHeight;
        }
        if (field is AutoCompleteBox selector) EditorAutoCompleteService.KeepDropDownAnchored(selector);
        return field;
    }

    private void FinalizeTechnologyPresentation()
    {
        ConfigureTechnologyField(_techNameBox, EditorFieldWidthRole.SingleLineText);
        foreach (var root in new[] { _propertiesPanel, _prereqsPanel, _effectsPanel })
        {
            foreach (var field in root.GetLogicalDescendants().OfType<Control>())
            {
                if (EditorFormLayout.TryGetWidthRole(field, out _)) continue;
                if (field is ComboBox) ConfigureTechnologyField(field, EditorFieldWidthRole.ShortEnum);
                else if (field is AutoCompleteBox) ConfigureTechnologyField(field, EditorFieldWidthRole.Reference);
                else if (field is TextBox { AcceptsReturn: false, IsVisible: true }) ConfigureTechnologyField(field, EditorFieldWidthRole.SingleLineText);
                else if (field is AssetPathEditor) ConfigureTechnologyField(field, EditorFieldWidthRole.AssetPath);
            }
            // Reparent presentation only; all callbacks and XML ownership stay with
            // the existing controls. Innermost rows are grouped before outer flows.
            foreach (var row in root.GetLogicalDescendants().OfType<WrapPanel>().Reverse().ToArray())
                GroupTechnologyFlowRow(row);
            foreach (var grid in root.GetLogicalDescendants().OfType<Grid>()) EditorFormLayout.NormalizeFieldGrid(grid);
        }
    }

    private static bool TechnologyInput(Control field)
        => field is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor;

    private static bool TechnologyCross(Control field)
        => field is Button button && button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card");

    private static void GroupTechnologyFlowRow(WrapPanel row)
        => EditorFormLayout.GroupFlowingFields(row);
}
