using AoMDivineDataEditor.Classes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AoMDivineDataEditor.Controls;

/// <summary>Resource selection and row lifecycle; the host owns field keys, validation and XML.</summary>
internal sealed class ResourceRowsEditor : StackPanel
{
    internal sealed class Row(string resource, string? originalResource, ComboBox type, TextBox value, StackPanel panel)
    {
        internal string Resource { get; set; } = resource;
        internal string? OriginalResource { get; } = originalResource;
        internal ComboBox Type { get; } = type;
        internal TextBox Value { get; } = value;
        internal StackPanel Panel { get; } = panel;
    }

    private readonly List<Row> _rows = [];
    private readonly WrapPanel _flow = new();
    private readonly Func<string, TextBox> _createValue;
    private readonly Func<Task<bool>> _tryEdit;
    private readonly Action _changed;
    private readonly bool _readOnly;
    private readonly string _defaultValue;
    private bool _refreshing;
    private bool _changing;
    internal IReadOnlyList<Row> Rows => _rows;
    internal Button AddButton { get; }
    internal event Action<string?, string?>? ResourceChanged;

    internal ResourceRowsEditor(IEnumerable<(string Resource, string Value)> initial, bool readOnly,
        string defaultValue, Func<string, TextBox> createValue, Func<Task<bool>> tryEdit, Action changed)
    {
        _readOnly = readOnly; _defaultValue = defaultValue; _createValue = createValue;
        _tryEdit = tryEdit; _changed = changed;
        Spacing = EditorFormLayout.LabelGap;
        Children.Add(_flow);
        AddButton = new Button
        {
            Content = "Add Resource", Classes = { "add-component", "inline-action" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap)
        };
        if (!readOnly) _flow.Children.Add(AddButton);
        foreach (var (resource, value) in initial) AddRow(resource, value, resource);
        RefreshOptions();
        AddButton.Click += async (_, _) =>
        {
            if (_changing || !Available().Any()) return;
            _changing = true;
            try
            {
                if (!await _tryEdit()) return;
                var resource = Available().FirstOrDefault();
                if (resource == null) return;
                AddRow(resource, _defaultValue, null);
                RefreshOptions(); ResourceChanged?.Invoke(null, resource); _changed();
            }
            finally { _changing = false; }
        };
    }

    private IEnumerable<string> Available(Row? current = null) => ProtoConstants.KnownResourceTypes
        .Where(resource => !_rows.Any(row => !ReferenceEquals(row, current) && row.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase)));

    private void RefreshOptions()
    {
        _refreshing = true;
        try
        {
            foreach (var row in _rows)
            {
                row.Type.ItemsSource = Available(row).ToArray();
                row.Type.SelectedItem = row.Resource;
            }
            AddButton.IsVisible = !_readOnly && Available().Any();
        }
        finally { _refreshing = false; }
    }

    private void AddRow(string resource, string value, string? original)
    {
        var type = new ComboBox { IsEnabled = !_readOnly };
        EditorFormLayout.ApplyWidth(type, EditorFieldWidthRole.ShortEnum);
        var input = _createValue(value);
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, EditorFormLayout.GroupGap, EditorFormLayout.LabelGap)
        };
        type.Margin = new Thickness(0, 0, EditorFormLayout.LabelGap, 0);
        panel.Children.Add(type); panel.Children.Add(input);
        var row = new Row(resource, original, type, input, panel);
        _rows.Add(row);
        var addIndex = _flow.Children.IndexOf(AddButton);
        _flow.Children.Insert(addIndex < 0 ? _flow.Children.Count : addIndex, panel);
        type.SelectionChanged += async (_, _) =>
        {
            if (_refreshing || _changing || type.SelectedItem is not string selected || selected == row.Resource) return;
            _changing = true;
            try
            {
                if (!Available(row).Contains(selected, StringComparer.OrdinalIgnoreCase) || !await _tryEdit()) return;
                var previous = row.Resource;
                row.Resource = selected;
                ResourceChanged?.Invoke(previous, selected); _changed();
            }
            finally { _changing = false; RefreshOptions(); }
        };
        if (!_readOnly)
        {
            var remove = EditorRemoveButtons.Row(description: "Remove resource");
            remove.Click += async (_, _) =>
            {
                if (_changing) return;
                _changing = true;
                try
                {
                    if (!await _tryEdit()) return;
                    _rows.Remove(row); _flow.Children.Remove(panel);
                    RefreshOptions(); ResourceChanged?.Invoke(row.Resource, null); _changed();
                }
                finally { _changing = false; }
            };
            panel.Children.Add(remove);
        }
    }
}
