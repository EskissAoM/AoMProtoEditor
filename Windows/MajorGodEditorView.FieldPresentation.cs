using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView
{
    private static Border CreateStartingGroupCard(Control content)
    {
        var modeRow = content is StackPanel stack ? (Panel)stack.Children[0] : (Panel)content;
        var remove = modeRow.Children.OfType<Button>().SingleOrDefault(button => button.Classes.Contains("remove-button"));
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        shell.Children.Add(content);
        if (remove != null)
        {
            modeRow.Children.Remove(remove);
            remove.Classes.Add("remove-card");
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(remove, 1);
            shell.Children.Add(remove);
        }
        return new Border { Classes = { "editor-card", "starting-group-card" }, Child = shell };
    }

    private T ConfigureMajorGodField<T>(T field, EditorFieldWidthRole role) where T : Control
    {
        EditorFormLayout.ApplyWidth(field, role);
        EditorFieldInteraction.Configure(field, role, () => _isPopulating);
        if (field is AutoCompleteBox or ComboBox || field is TextBox { AcceptsReturn: false })
            field.MinHeight = field.Height = field.MaxHeight = EditorFormLayout.FieldHeight;
        if (field is AutoCompleteBox picker) EditorAutoCompleteService.KeepDropDownAnchored(picker);
        return field;
    }

    private void FinalizeMajorGodPresentation()
    {
        // Explicit rebuild/population boundaries; never scan on LayoutUpdated.
        var controls = this.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
        foreach (var row in controls.OfType<WrapPanel>().Reverse()) EditorFormLayout.GroupFlowingFields(row);
        foreach (var grid in controls.OfType<Grid>()) EditorFormLayout.NormalizeFieldGrid(grid);
        foreach (var row in controls.OfType<StackPanel>().Where(row => row.Orientation == Orientation.Horizontal &&
                     row.Children.Any(child => child is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor)))
            EditorFormLayout.UseInlineRowLayout(row);
        DecorateMajorGodAdditiveFields();
    }
}
