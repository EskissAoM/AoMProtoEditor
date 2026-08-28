using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared visual row for a modifier. The host retains modify-type semantics,
/// damage-type choices, numeric validation, source locking, and XML mapping.
/// </summary>
public sealed class ModifierEditor : WrapPanel
{
    public Control ModifyTypeField { get; }
    public Control? ApplyTypeField { get; }
    public Control? DamageTypeField { get; }
    public Control ValueField { get; }
    public StackPanel? DamageTypeGroup { get; }
    public TextBlock? DamageTypeLabel { get; }
    public Button? RemoveButton { get; }

    public ModifierEditor(
        Control modifyTypeField,
        Control valueField,
        Control? damageTypeField = null,
        Control? applyTypeField = null,
        string modifyLabel = "Modify:",
        string damageTypeLabel = "Damage Type:",
        string? applyTypeLabel = null,
        string? valueLabel = "Value:",
        bool showRemoveButton = true)
    {
        ModifyTypeField = modifyTypeField;
        ApplyTypeField = applyTypeField;
        DamageTypeField = damageTypeField;
        ValueField = valueField;

        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0, 2, 0, 2);

        Children.Add(CreateGroup(modifyLabel, ModifyTypeField));

        if (DamageTypeField != null)
        {
            DamageTypeGroup = CreateGroup(damageTypeLabel, DamageTypeField);
            DamageTypeLabel = (TextBlock)DamageTypeGroup.Children[0];
            Children.Add(DamageTypeGroup);
        }

        if (ApplyTypeField != null)
            Children.Add(CreateGroup(applyTypeLabel, ApplyTypeField));

        Children.Add(CreateGroup(valueLabel, ValueField, new Thickness(0, 0, 0, 4)));

        if (showRemoveButton)
        {
            RemoveButton = new Button
            {
                Classes = { "remove-button" },
                Margin = new Thickness(2, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(RemoveButton);
        }
    }

    public void SetDamageTypeVisible(bool isVisible)
    {
        if (DamageTypeGroup != null)
            DamageTypeGroup.IsVisible = isVisible;
    }

    private static StackPanel CreateGroup(
        string? label,
        Control field,
        Thickness? margin = null)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = string.IsNullOrWhiteSpace(label) ? 0 : 8,
            Margin = margin ?? new Thickness(0, 0, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(label))
        {
            group.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        field.VerticalAlignment = VerticalAlignment.Center;
        group.Children.Add(field);
        return group;
    }
}
