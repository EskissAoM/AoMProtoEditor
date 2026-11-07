using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView
{
    internal void UpdateIconCatalog(IEnumerable<string> paths)
    {
        var loading = _loadingUi;
        _loadingUi = true;
        try { if (_iconPaths.ReplaceWith(paths)) SelectorCatalogRefresh.Apply(this); }
        finally { _loadingUi = loading; }
    }
}

public partial class MajorGodEditorView
{
    internal void UpdateIconCatalog(IEnumerable<string> paths)
    {
        var populating = _isPopulating;
        _isPopulating = true;
        try { if (_iconPaths.ReplaceWith(paths)) SelectorCatalogRefresh.Apply(this); }
        finally { _isPopulating = populating; }
    }
}

public partial class PowerEditorView
{
    internal void UpdateIconCatalog(IEnumerable<string> paths)
    {
        var populating = _isPopulating;
        _isPopulating = true;
        try { if (_catalogs.RefreshIcons(paths)) SelectorCatalogRefresh.Apply(this); }
        finally { _isPopulating = populating; }
    }
}
