using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Windows;

internal sealed partial class ProtoUnitCommandEditorWindow
{
    private static bool LongCommandRollover(string tag)
        => tag is "rollovertextid" or "activerollovertextid" or "disabledrollovertextid";

    private Control ConfigureCommandField(Control field, string tag)
    {
        if (field is TextBox text && LongCommandRollover(tag))
        {
            return EditorRolloverField.Configure(text);
        }
        var role = tag.ToLowerInvariant() switch
        {
            "amount" => EditorFieldWidthRole.Numeric,
            "shortrollovertextid" or "buildlimittextid" => EditorFieldWidthRole.ShortRollover,
            "age" => EditorFieldWidthRole.LongEnum,
            "icon" or "controllericon" or "activeicon" or "disabledicon" => EditorFieldWidthRole.AssetPath,
            "associatedtech" or "forbidtech" or "trainableunitreq" or "protounit" or "costprotounit" or
                "prereqcommand" or "associatedpower" or "actionforactiveicon" or "command" or "secondarycommand"
                => EditorFieldWidthRole.Reference,
            _ => EditorFieldWidthRole.SingleLineText
        };
        return ProtoUnitCommandFieldFactory.ConfigureField(field, role, () => _refreshingPreview);
    }

    private StackPanel CreateCommandFieldGroup(string tag, bool leading = false, Button? cross = null)
    {
        var editor = CreateValueEditor(tag);
        _valueEditors[tag] = editor;
        var group = EditorFormLayout.LabeledGroup(Labels.GetValueOrDefault(tag, tag), editor);
        if (leading) ((TextBlock)group.Children[0]).Width = 115; // Existing compact modal label column.
        if (cross != null) group.Children.Add(cross);
        EditorFormLayout.UseInlineRowLayout(group);
        return group;
    }

    private static void WrapCommandGrid(Grid row, params int[][] groups)
    {
        EditorFormLayout.WrapFlowingGridColumns(row, groups);
        foreach (var group in ((WrapPanel)row.Children[0]).Children.OfType<StackPanel>())
            EditorFormLayout.UseInlineRowLayout(group);
    }
}
