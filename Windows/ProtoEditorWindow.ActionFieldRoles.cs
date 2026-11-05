using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private static Control CreateModifierRampValueCaption() => new Border
    {
        Padding = new Avalonia.Thickness(0, 0, EditorFormLayout.LabelGap, 0),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = EditorFormLayout.LabelGap,
            Children = { new TextBlock { Text = "Value" }, new TextBlock { Text = "Initial:" } }
        }
    };

    private Panel CreateProtoActionStructuredRowsPanel(ProtoActionWidgetState state, string tag)
        => tag.Equals("rate", StringComparison.OrdinalIgnoreCase)
            ? new CompactRowsPanel(row => state.StructuredFieldRows[tag].Any(entry =>
                ReferenceEquals(entry.RowPanel, row) && !entry.HasUnrepresentedAttributes &&
                entry.AttributeEditors.Keys.Concat(entry.PreservedAttributes.Keys)
                    .All(attribute => attribute.Equals("type", StringComparison.OrdinalIgnoreCase))))
            : new StackPanel { Spacing = 4 };

    private void ApplyProtoActionAttributeWidth(Control editor, string tag, string attribute)
    {
        if (_isReadOnly || !editor.IsEnabled)
        {
            EditorFieldInteraction.Configure(editor, EditorFieldWidthRole.Reference, () => _isPopulating);
            return;
        }
        var role = attribute.ToLowerInvariant() switch
        {
            "frequency" or "yield" or "inventoryrate" or "count" or "weight" or "prob" or "chance" or "amount" or "factor" when editor is TextBox => EditorFieldWidthRole.Numeric,
            "class" when tag.Equals("sizeclassanim", StringComparison.OrdinalIgnoreCase) => EditorFieldWidthRole.Numeric,
            "resource" or "overrideresource" => EditorFieldWidthRole.ShortEnum,
            _ when editor is ComboBox => EditorFieldWidthRole.ShortEnum,
            _ when EditorFormLayout.TryGetWidthRole(editor, out var configured) => configured,
            _ when editor is AutoCompleteBox => EditorFieldWidthRole.Reference,
            _ => EditorFieldWidthRole.SingleLineText
        };
        ConfigureUnitField(editor, role);
    }

    // Finalize the fields registered by the shared action builders, including
    // controls rebuilt after an optional section opens. The model supplies tags;
    // presentation never infers a role from the current value or its length.
    private void FinalizeProtoActionFieldRoles(ProtoActionWidgetState state)
    {
        void Set(Control editor, EditorFieldWidthRole role)
        {
            if (_isReadOnly || !editor.IsEnabled)
            {
                EditorFieldInteraction.Configure(editor, role, () => _isPopulating);
                return;
            }
            ConfigureUnitField(editor, role);
            if (editor is AutoCompleteBox box) EditorAutoCompleteService.KeepDropDownAnchored(box);
            if (state.SourceMarkerHosts.TryGetValue(editor, out var host))
            {
                host.Width = editor.Width;
                host.MinWidth = editor.MinWidth;
                host.MaxWidth = editor.MaxWidth;
            }
        }
        void Numeric(params TextBox?[] fields)
        {
            foreach (var field in fields) if (field != null) Set(field, EditorFieldWidthRole.Numeric);
        }
        void Reference(params Control[] fields)
        {
            foreach (var field in fields) Set(field, EditorFieldWidthRole.Reference);
        }
        void Enum(params Control[] fields)
        {
            foreach (var field in fields) Set(field, EditorFieldWidthRole.LongEnum);
        }
        void Tagged(Control editor, string tag)
        {
            ApplyProtoActionFieldWidth(editor, tag);
            if (EditorFormLayout.TryGetWidthRole(editor, out var role)) Set(editor, role);
        }

        Set(state.NameAcb, EditorFieldWidthRole.Reference);
        Set(state.TypeAcb, EditorFieldWidthRole.LongEnum);
        Numeric(state.RofTb, state.MaxRangeTb, state.JumpAttackMinRangeTb);
        foreach (var (tag, editor) in state.AdditionalFieldControls) Tagged(editor, tag);
        foreach (var (editor, tag) in state.MirroredSimpleSourceControls) Tagged(editor, tag);
        foreach (var (tag, rows) in state.StructuredFieldRows)
        foreach (var row in rows)
        {
            Tagged(row.ValueTb, tag);
            foreach (var (attribute, editor) in row.AttributeEditors)
            {
                ApplyProtoActionAttributeWidth(editor, tag, attribute);
                if (EditorFormLayout.TryGetWidthRole(editor, out var role)) Set(editor, role);
            }
        }
        foreach (var row in state.DamageRows)
        {
            Set(row.TypeCb, EditorFieldWidthRole.ShortEnum); Numeric(row.ValTb);
        }
        foreach (var row in state.BonusRows)
        {
            Reference(row.TypeAcb); Set(row.ValTb, EditorFieldWidthRole.DamageBonusMultiplier);
        }
        foreach (var target in state.EmpowerTargetStates.Values.SelectMany(value => value))
        {
            Reference(target.TargetAcb, target.AnimAcb); Numeric(target.EmpowerAreaTb);
            Set(target.ModelAttachmentEditor, EditorFieldWidthRole.CompactAssetPath);
            Set(target.ModelAttachmentBoneAcb, EditorFieldWidthRole.AttachmentBone);
            foreach (var field in target.ForbidTypeAcbs) Reference(field);
            foreach (var rate in target.RateRows)
            {
                Enum(rate.ModifyTypeAcb); Set(rate.DamageTypeCb, EditorFieldWidthRole.ShortEnum); Numeric(rate.ValueTb);
            }
        }
        foreach (var row in state.ChargedRows)
        {
            Enum(row.ModifyTypeAcb); Set(row.ApplyTypeCb, EditorFieldWidthRole.ShortEnum); Set(row.ParamCb, EditorFieldWidthRole.ShortEnum);
            Numeric(row.ValueTb, row.ModifyAmountCapTb);
        }
        foreach (var row in state.FullChargedRows)
        {
            Enum(row.ModifyTypeAcb, row.ActivationTypeCb, row.TerrainTypeCb); Set(row.ApplyTypeCb, EditorFieldWidthRole.ShortEnum);
            Set(row.ParamCb, EditorFieldWidthRole.ShortEnum);
            Numeric(row.ValueTb, row.CooldownTb, row.DurationTb, row.ThresholdTb, row.VfxDurationTb);
            Reference(row.VfxAcb, row.SetTacticTb);
            Set(row.ChargedModelAttachmentEditor, EditorFieldWidthRole.CompactAssetPath);
            Set(row.ChargedModelAttachmentBoneAcb, EditorFieldWidthRole.AttachmentBone);
        }
        foreach (var row in state.OnHitEffectRows)
        {
            Enum(row.TypeCb, row.PlayerRelationAcb, row.TerrainTypeCb);
            foreach (var compact in new Control[] { row.TargetModeCb, row.RewardTypeCb, row.RelativityCb }) Set(compact, EditorFieldWidthRole.ShortEnum);
            Reference(row.TargetUnitTypeAcb, row.AnimAcb, row.ProtoActionAcb, row.AttachProtoAcb, row.ByUnitTypeAcb);
            Set(row.AttachBoneAcb, EditorFieldWidthRole.AttachmentBone);
            Set(row.ShadingTypeAcb, EditorFieldWidthRole.ShortEnum); Set(row.FreezeTypeCb, EditorFieldWidthRole.LongEnum);
            Numeric(row.DurationTb, row.DelayTb, row.RadiusTb, row.StackLimitTb, row.RateTb,
                row.ProbTb, row.GlobalProbTb, row.AmountTb, row.FadeTimeTb, row.FactorTb, row.FreezeDurationTb, row.FreezeDamageTb);
            foreach (var field in row.ChildAttackTypeAcbs.Concat(row.ChildIgnoreTypeAcbs)) Reference(field);
            foreach (var damage in row.DamageRows)
            {
                Set(damage.DamageTypeCb, EditorFieldWidthRole.ShortEnum); Numeric(damage.ValueTb);
            }
            foreach (var modify in row.ModifyRows)
            {
                Enum(modify.ModifyTypeAcb); Set(modify.ApplyTypeCb, EditorFieldWidthRole.ShortEnum); Set(modify.DamageTypeCb, EditorFieldWidthRole.ShortEnum); Numeric(modify.ValueTb);
            }
            foreach (var ramp in row.ModifyRampRows)
            {
                Enum(ramp.ModifyTypeAcb); Set(ramp.DamageTypeCb, EditorFieldWidthRole.ShortEnum); Numeric(ramp.InitTb, ramp.FinalTb);
            }
        }
        foreach (var row in state.Container.GetLogicalDescendants().OfType<StackPanel>())
            if (row.Orientation == Orientation.Horizontal && row.Children.OfType<Button>().Any(button => button.Classes.Contains("remove-button")) &&
                row.Children.Any(child => child is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor ||
                    child is Grid grid && grid.GetLogicalDescendants().Any(field => field is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor)))
                EditorFormLayout.UseInlineRowLayout(row);
        foreach (var row in state.Container.GetLogicalDescendants().OfType<WrapPanel>().Reverse().ToArray())
            EditorFormLayout.GroupFlowingFields(row);
        foreach (var grid in state.Container.GetLogicalDescendants().OfType<Grid>())
            EditorFormLayout.NormalizeFieldGrid(grid);
        // Markers wrap inputs in single-cell grids; those hosts need the same
        // allocation as the input before a following checkbox is measured.
        foreach (var host in state.SourceMarkerHosts.Values)
            if (host.Parent is Grid parent && Grid.GetColumn(host) < parent.ColumnDefinitions.Count)
                parent.ColumnDefinitions[Grid.GetColumn(host)].Width = GridLength.Auto;
    }
}
