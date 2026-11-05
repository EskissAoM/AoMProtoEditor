using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    // Shared Unit Stats reference picker: the caller owns the values and XML mapping.
    // Keep lists as lists so existing duplicates can still be reported on Save.
    private StackPanel CreateStatsCatalogChipGroup(string title, ICollection<string> values,
        IEnumerable<string> catalog, out AutoCompleteBox? picker, bool inlineChips = false,
        Action<AutoCompleteBox>? valuesChanged = null)
    {
        var group = new StackPanel { Spacing = 2 };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = EditorFormLayout.LabelGap,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (inlineChips)
        {
            header.Margin = new Thickness(0, 0, EditorFormLayout.LabelGap, 0);
            chips.Children.Add(header);
        }
        else group.Children.Add(header);
        group.Children.Add(chips);
        var source = catalog;
        var unitName = _currentUnitName;
        AutoCompleteBox? input = null;
        bool changing = false;
        bool Contains(string value) => values.Contains(value, StringComparer.OrdinalIgnoreCase);
        bool StillCurrent() => unitName == _currentUnitName &&
            group.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, this));

        void Refresh()
        {
            if (input != null)
                EditorAutoCompleteService.SetCatalogSuggestions(input, source.Where(value => !Contains(value)));
            foreach (var child in chips.Children.Where(child => !ReferenceEquals(child, header)).ToArray())
                chips.Children.Remove(child);
            foreach (var value in values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray())
                chips.Children.Add(CreateChip(value, async () =>
                {
                    if (changing || !StillCurrent()) return;
                    changing = true;
                    try
                    {
                        if (!await CheckStartLocalMod() || !StillCurrent()) return;
                        values.Remove(value);
                        if (input != null) valuesChanged?.Invoke(input);
                        Refresh();
                        MarkDirty();
                        CommitChipAdditionAndRefreshXmlPreview();
                    }
                    finally { changing = false; }
                }));
        }

        if (!_isReadOnly)
        {
            input = new AutoCompleteBox
            {
                FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0,
                MinimumPopulateDelay = TimeSpan.Zero, Margin = new Thickness(0)
            };
            ConfigureUnitField(input, EditorFieldWidthRole.ChipSearch);
            EnableDropdownAutoComplete(input, selectAllOnFirstClick: false);
            EditorAutoCompleteService.KeepDropDownAnchored(input);
            input.SelectionChanged += async (_, _) =>
            {
                if (changing || _isPopulating || input.SelectedItem is not string selected || !StillCurrent()) return;
                var match = source.FirstOrDefault(value => value.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (match == null || Contains(match)) return;
                changing = true;
                try
                {
                    if (!await CheckStartLocalMod() || !StillCurrent() || Contains(match)) return;
                    values.Add(match);
                    valuesChanged?.Invoke(input);
                    input.Text = "";
                    input.SelectedItem = null;
                    Refresh();
                    MarkDirty();
                    CommitChipAdditionAndRefreshXmlPreview();
                }
                finally { changing = false; }
            };
            header.Children.Add(input);
        }
        Refresh();
        picker = input;
        return group;
    }

    private IEnumerable<ProtoBuildLimitEntry> ReadBuildLimitEntries()
        => _currentBuildLimitMode == BuildLimitMode.Dynamic
            ? _dynamicBuildLimitTypes.Select(value => new ProtoBuildLimitEntry { Value = value })
            : _buildLimitRows.Select(row => new ProtoBuildLimitEntry
            {
                Value = row.ValueAcb.Text?.Trim() ?? "", Weight = row.WeightTb?.Text?.Trim() ?? ""
            });
}
