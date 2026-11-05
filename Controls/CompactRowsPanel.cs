using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Controls;

/// <summary>At most two complete compact rows per line; richer rows keep their own line.</summary>
internal sealed class CompactRowsPanel(Func<Control, bool> canShareLine) : Panel
{
    private const double RowSpacing = 4;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return LayoutRows(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutRows(finalSize.Width, true);
        return finalSize;
    }

    private Size LayoutRows(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, usedWidth = 0;
        var count = 0;
        void NextLine()
        {
            if (count == 0) return;
            y += lineHeight + RowSpacing;
            x = 0; lineHeight = 0; count = 0;
        }
        foreach (var child in Children.Where(child => child.IsVisible))
        {
            var add = child is Button button && button.Classes.Contains("add-component");
            var compact = add || canShareLine(child);
            var size = child.DesiredSize;
            if (count > 0 && (!compact || count == 2 || x + size.Width > width)) NextLine();
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            usedWidth = Math.Max(usedWidth, x + size.Width);
            lineHeight = Math.Max(lineHeight, size.Height);
            count++;
            x += size.Width + EditorFormLayout.GroupGap;
            if (!compact || add) NextLine();
        }
        // A completed full row already includes the following inter-row gap.
        var height = count > 0 ? y + lineHeight : Math.Max(0, y - RowSpacing);
        return new Size(usedWidth, height);
    }
}
