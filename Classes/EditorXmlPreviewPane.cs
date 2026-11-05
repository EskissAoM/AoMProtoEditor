using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Shared pane geometry only; XML sessions and dirty tracking remain with the editor.</summary>
public sealed class EditorXmlPreviewPane
{
    public const double MinimumWidth = 28;
    private static readonly ConditionalWeakTable<Grid, EditorXmlPreviewPane> Panes = new();
    private readonly Grid _grid;
    private readonly int _formColumn;
    private readonly int _splitColumn;
    private readonly int _previewColumn;
    private readonly Control _content;
    private readonly GridSplitter _splitter;
    private readonly Button _toggle;
    private GridLength _formWidth;
    private GridLength _previewWidth;
    private GridLength _splitWidth;
    private bool _collapsed;

    private EditorXmlPreviewPane(Grid grid, int formColumn, int splitColumn, int previewColumn,
        Control content, GridSplitter splitter, Button toggle)
    {
        _grid = grid; _formColumn = formColumn; _splitColumn = splitColumn; _previewColumn = previewColumn;
        _content = content; _splitter = splitter; _toggle = toggle;
        grid.ColumnDefinitions[previewColumn].MinWidth = MinimumWidth;
        CaptureDefaultSplit();
    }

    public static void Configure(Grid grid, int formColumn, int splitColumn, int previewColumn,
        Control content, GridSplitter splitter, Button toggle)
        => Panes.GetValue(grid, _ => new EditorXmlPreviewPane(grid, formColumn, splitColumn, previewColumn, content, splitter, toggle));

    public static void SetCollapsed(Grid grid, bool collapsed) => Panes.GetValue(grid,
        _ => throw new InvalidOperationException("XML preview pane has not been configured.")).SetCollapsed(collapsed);

    public static void CaptureDefaultSplit(Grid grid) => Panes.GetValue(grid,
        _ => throw new InvalidOperationException("XML preview pane has not been configured.")).CaptureDefaultSplit();

    private void CaptureDefaultSplit()
    {
        var columns = _grid.ColumnDefinitions;
        _formWidth = columns[_formColumn].Width;
        _previewWidth = columns[_previewColumn].Width;
        _splitWidth = columns[_splitColumn].Width;
    }

    private void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed) return;
        var columns = _grid.ColumnDefinitions;
        if (collapsed)
        {
            // Dragging is temporary; the arrow restores this editor context's default.
            columns[_formColumn].Width = new GridLength(1, GridUnitType.Star);
            columns[_splitColumn].Width = new GridLength(0);
            columns[_previewColumn].Width = new GridLength(MinimumWidth);
        }
        else
        {
            columns[_formColumn].Width = _formWidth;
            columns[_splitColumn].Width = _splitWidth;
            columns[_previewColumn].Width = _previewWidth;
        }
        _collapsed = collapsed;
        _content.IsVisible = _splitter.IsVisible = !collapsed;
        _toggle.Content = collapsed ? "◀" : "▶";
        ToolTip.SetTip(_toggle, collapsed ? "Restore XML Preview" : "Collapse XML Preview");
    }
}
