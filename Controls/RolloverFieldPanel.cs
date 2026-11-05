using Avalonia;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>Measures text against a finite budget even when an outer flow offers infinity.</summary>
internal sealed class RolloverFieldPanel : Panel
{
    internal TextBox Field { get; }
    internal Button? Cross { get; }

    internal RolloverFieldPanel(TextBox field, Button? cross)
    {
        Field = field; Cross = cross;
        Children.Add(field);
        if (cross != null) Children.Add(cross);
    }

    private double CrossWidth => Cross is { IsVisible: true } ? Cross.DesiredSize.Width : 0;
    private double Gap => CrossWidth > 0 ? EditorFormLayout.CrossGap : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        Cross?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(0, Math.Min(EditorRolloverField.StandardWidth, availableSize.Width - CrossWidth - Gap));
        Field.Measure(new Size(width, availableSize.Height));
        return new Size(width + CrossWidth + Gap, Math.Max(Field.DesiredSize.Height, Cross?.DesiredSize.Height ?? 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Max(0, Math.Min(EditorRolloverField.StandardWidth, finalSize.Width - CrossWidth - Gap));
        Field.Arrange(new Rect(0, 0, width, Field.DesiredSize.Height));
        if (Cross is { IsVisible: true })
            Cross.Arrange(new Rect(width + Gap, Math.Max(0, (EditorFormLayout.FieldHeight - Cross.DesiredSize.Height) / 2), CrossWidth, Cross.DesiredSize.Height));
        return finalSize;
    }
}
