using System.Collections;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>A stable catalog identity shared by selectors and save validators.
/// Owners replace its values under their population guard, then refresh controls.
/// It performs no discovery, persistence or automatic changes to selected values.</summary>
public sealed class SelectorCatalog : IReadOnlyList<string>
{
    private readonly List<string> _values;
    public SelectorCatalog(IEnumerable<string>? values) => _values = Normalize(values);
    public int Count => _values.Count;
    public string this[int index] => _values[index];
    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal bool ReplaceWith(IEnumerable<string> values)
    {
        var updated = Normalize(values);
        if (_values.SequenceEqual(updated, StringComparer.Ordinal)) return false;
        _values.Clear(); _values.AddRange(updated);
        return true;
    }

    private static List<string> Normalize(IEnumerable<string>? values)
        => (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
}

internal static class SelectorCatalogRefresh
{
    // The owning view must hold its normal population guard. A refresh changes
    // suggestions, not form values, XML, dirty state or the active raw buffer.
    internal static void Apply(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>()
                     .Concat(root.GetVisualDescendants().OfType<Control>()).Prepend(root).Distinct())
        {
            if (control is AutoCompleteBox box) EditorAutoCompleteService.RefreshCatalogSuggestions(box);
            else if (control is StrictSearchableComboBox strict) strict.RefreshCatalogOptions();
        }
    }
}
