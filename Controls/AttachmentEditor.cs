using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared presentation for an attachment path and its bone. XML tag ownership,
/// optional visibility, timers, flags, and removal remain with the host action.
/// </summary>
public sealed class AttachmentEditor : WrapPanel
{
    public const double AttachmentWidth = 200;
    public const double BoneWidth = 100;

    public Control AttachmentField { get; }
    public Control BoneField { get; }

    public AttachmentEditor(
        Control attachmentField,
        Control boneField,
        string attachmentLabel = "Model Attachment:",
        string boneLabel = "Bone:",
        Thickness? rowMargin = null,
        Thickness? attachmentGroupMargin = null,
        Thickness? boneGroupMargin = null)
    {
        AttachmentField = attachmentField;
        BoneField = boneField;

        Orientation = Orientation.Horizontal;
        Margin = rowMargin ?? new Thickness(0, 2, 0, 2);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        ConfigureField(AttachmentField, AttachmentWidth);
        ConfigureField(BoneField, BoneWidth);
        Children.Add(CreateLabeledFieldGroup(attachmentLabel, AttachmentField, attachmentGroupMargin));
        Children.Add(CreateLabeledFieldGroup(boneLabel, BoneField, boneGroupMargin));
    }

    private static void ConfigureField(Control field, double width)
    {
        field.Width = width;
        field.HorizontalAlignment = HorizontalAlignment.Left;
        field.VerticalAlignment = VerticalAlignment.Center;
    }

    private static StackPanel CreateLabeledFieldGroup(
        string label,
        Control editor,
        Thickness? margin = null)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = margin ?? new Thickness(0, 0, 12, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        group.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        group.Children.Add(editor);
        return group;
    }
}
