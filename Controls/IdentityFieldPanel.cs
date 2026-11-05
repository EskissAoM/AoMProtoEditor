using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>Bounds the identity text and keeps its preview beside the content, not the window edge.</summary>
internal sealed class IdentityFieldPanel(Control fields, Control icon) : Panel
{
    internal Control Fields { get; } = fields;
    internal Control Icon { get; } = icon;
    internal bool IsStacked { get; private set; }
    private double _fieldsWidth;

    internal void Initialize()
    {
        Children.Add(Fields);
        Children.Add(Icon);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var crossSpace = 0d;
        foreach (var host in Fields.GetLogicalDescendants().OfType<RolloverFieldPanel>().Where(host => host.IsEffectivelyVisible))
            if (host.Cross is { IsVisible: true } cross)
            {
                cross.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                crossSpace = Math.Max(crossSpace, cross.DesiredSize.Width + EditorFormLayout.CrossGap);
            }
        var fieldsBudget = EditorFormLayout.LabelColumnWidth + EditorRolloverField.StandardWidth + crossSpace;
        IsStacked = availableSize.Width < fieldsBudget + Icon.DesiredSize.Width;
        var budget = Math.Max(0, Math.Min(fieldsBudget,
            availableSize.Width - (IsStacked ? 0 : Icon.DesiredSize.Width)));
        Fields.Measure(new Size(budget, availableSize.Height));
        _fieldsWidth = Math.Min(budget, Fields.DesiredSize.Width);
        return new Size(IsStacked ? Math.Max(_fieldsWidth, Icon.DesiredSize.Width) : _fieldsWidth + Icon.DesiredSize.Width,
            IsStacked ? Fields.DesiredSize.Height + Icon.DesiredSize.Height + EditorFormLayout.GroupGap
                : Math.Max(Fields.DesiredSize.Height, Icon.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, Math.Min(_fieldsWidth, finalSize.Width - (IsStacked ? 0 : Icon.DesiredSize.Width)));
        Fields.Arrange(new Rect(0, IsStacked ? Icon.DesiredSize.Height + EditorFormLayout.GroupGap : 0,
            width, Fields.DesiredSize.Height));
        Icon.Arrange(new Rect(IsStacked ? Math.Max(0, width - Icon.DesiredSize.Width) : width, 0,
            Icon.DesiredSize.Width, Icon.DesiredSize.Height));
        return finalSize;
    }
}
