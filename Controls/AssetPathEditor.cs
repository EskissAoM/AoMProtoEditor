using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CryBarEditor.Classes;

namespace CryBarEditor.Controls;

public sealed class AssetPathEditor : Grid
{
    private bool _editing;
    private bool _suppress;
    private Func<string, Task>? _changed;

    public TextBox CompactPresenter { get; } = new()
    {
        IsReadOnly = true,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    };
    public AutoCompleteBox Editor { get; } = new()
    {
        FilterMode = AutoCompleteFilterMode.Contains,
        MinimumPrefixLength = 0,
        IsVisible = false
    };
    public string FullValue { get; private set; } = "";
    public IReadOnlyList<PathSuggestion> Suggestions { get; private set; } = [];
    public event EventHandler? FullValueChanged;
    public event EventHandler? EditingCompleted;

    public AssetPathEditor()
    {
        Children.Add(CompactPresenter);
        Children.Add(Editor);
        Editor.ItemTemplate = new FuncDataTemplate<PathSuggestion>((item, _) => new TextBlock { Text = item?.DisplayValue ?? "" });
        Editor.ItemFilter = (search, item) => item is PathSuggestion suggestion &&
            (suggestion.DisplayValue.Contains(search ?? "", StringComparison.OrdinalIgnoreCase) || suggestion.FullValue.Contains(search ?? "", StringComparison.OrdinalIgnoreCase));
        Editor.ItemSelector = (_, item) => item is PathSuggestion suggestion ? suggestion.FullValue : item?.ToString() ?? "";
        EditorTextFieldStyle.ConfigureSelector(Editor);
        Width = EditorTextFieldStyle.StandardWidth;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        CompactPresenter.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            if (IsEnabled)
                BeginEdit();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
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

        // Always begin in compact mode, including for an empty path.  Automatically
        // leaving an empty AutoCompleteBox in edit mode creates a focus race after its
        // first LostFocus (the next click can immediately be consumed by EndEdit).
        // Enter edit mode only from an explicit click, which also opens the complete
        // suggestion list because MinimumPrefixLength is zero.
        _editing = false;
        Editor.IsVisible = false;
        CompactPresenter.IsVisible = true;
    }

    private async Task SetValueAsync(string? value)
    {
        var next = value?.Trim() ?? ""; if (next == FullValue) return;
        FullValue = next;
        Refresh();
        FullValueChanged?.Invoke(this, EventArgs.Empty);
        if (_changed != null) await _changed(next);
    }
    private void BeginEdit()
    {
        _editing = true; CompactPresenter.IsVisible = false; Editor.IsVisible = true;
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
    private void EndEdit()
    {
        if (!_editing)
            return;

        _editing = false;
        Editor.IsVisible = false;
        CompactPresenter.IsVisible = true;
        Refresh();
        EditingCompleted?.Invoke(this, EventArgs.Empty);
    }
    private void Refresh()
    {
        CompactPresenter.Text = Suggestions.FirstOrDefault(x => x.FullValue.Equals(FullValue, StringComparison.OrdinalIgnoreCase))?.DisplayValue ?? FullValue;
        ToolTip.SetTip(this, FullValue);
    }
}
