using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CryBarEditor.Classes;

namespace CryBarEditor.Controls;

public sealed class AssetPathEditor : Grid
{
    private bool _editing;
    private bool _suppress;
    private Func<string, Task>? _changed;
    private readonly Border _compactBorder;

    public TextBlock CompactPresenter { get; } = new()
    {
        Background = Avalonia.Media.Brush.Parse("#1b1b1b"),
        Padding = new Avalonia.Thickness(8, 6),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    };
    public AutoCompleteBox Editor { get; } = new() { FilterMode = AutoCompleteFilterMode.Contains, IsVisible = false };
    public string FullValue { get; private set; } = "";
    public IReadOnlyList<PathSuggestion> Suggestions { get; private set; } = [];

    public AssetPathEditor()
    {
        _compactBorder = new Border
        {
            Background = CompactPresenter.Background,
            CornerRadius = new Avalonia.CornerRadius(3),
            MinHeight = 32,
            Child = CompactPresenter
        };
        Children.Add(_compactBorder);
        Children.Add(Editor);
        Editor.ItemTemplate = new FuncDataTemplate<PathSuggestion>((item, _) => new TextBlock { Text = item?.DisplayValue ?? "" });
        Editor.ItemFilter = (search, item) => item is PathSuggestion suggestion &&
            (suggestion.DisplayValue.Contains(search ?? "", StringComparison.OrdinalIgnoreCase) || suggestion.FullValue.Contains(search ?? "", StringComparison.OrdinalIgnoreCase));
        Editor.ItemSelector = (_, item) => item is PathSuggestion suggestion ? suggestion.FullValue : item?.ToString() ?? "";
        EditorTextFieldStyle.ConfigureSelector(Editor);
        Width = EditorTextFieldStyle.StandardWidth;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _compactBorder.PointerPressed += (_, _) => { if (IsEnabled) BeginEdit(); };
        Editor.SelectionChanged += async (_, _) =>
        {
            if (_suppress || Editor.SelectedItem is not PathSuggestion item) return;
            await SetValueAsync(item.FullValue);
            _suppress = true; try { Editor.Text = FullValue; } finally { _suppress = false; }
        };
        Editor.TextChanged += async (_, _) =>
        {
            if (!_editing || _suppress) return;
            if (Editor.SelectedItem is PathSuggestion item && !item.FullValue.Equals(Editor.Text, StringComparison.OrdinalIgnoreCase)) Editor.SelectedItem = null;
            await SetValueAsync(Editor.Text);
        };
        Editor.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!Editor.IsFocused && !Editor.IsDropDownOpen) EndEdit();
        });
    }

    public void Configure(string fullValue, IEnumerable<string> suggestions, Func<string, Task>? changed)
    {
        FullValue = fullValue?.Trim() ?? ""; _changed = changed;
        Suggestions = AssetPathDisplayService.CreateSuggestions(suggestions.Append(FullValue));
        Editor.ItemsSource = Suggestions;
        _suppress = true;
        try { Editor.Text = FullValue; } finally { _suppress = false; }
        Refresh();
        if (string.IsNullOrWhiteSpace(FullValue) && IsEnabled)
        {
            _editing = true;
            _compactBorder.IsVisible = false;
            Editor.IsVisible = true;
        }
    }

    private async Task SetValueAsync(string? value)
    {
        var next = value?.Trim() ?? ""; if (next == FullValue) return;
        FullValue = next;
        Refresh();
        if (_changed != null) await _changed(next);
    }
    private void BeginEdit()
    {
        _editing = true; _compactBorder.IsVisible = false; Editor.IsVisible = true;
        _suppress = true; try { Editor.Text = FullValue; } finally { _suppress = false; }
        Editor.Focus();
        Editor.IsDropDownOpen = true;
        Dispatcher.UIThread.Post(() =>
        {
            var textEditor = Editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (Editor.IsVisible && textEditor != null)
            {
                textEditor.Focus();
                textEditor.SelectAll();
            }
        }, DispatcherPriority.Input);
    }
    private void EndEdit() { _editing = false; Editor.IsVisible = false; _compactBorder.IsVisible = true; Refresh(); }
    private void Refresh()
    {
        CompactPresenter.Text = Suggestions.FirstOrDefault(x => x.FullValue.Equals(FullValue, StringComparison.OrdinalIgnoreCase))?.DisplayValue ?? FullValue;
        _compactBorder.Background = CompactPresenter.Background;
        ToolTip.SetTip(this, FullValue);
    }
}
