using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// An editable ComboBox whose text is only a case-insensitive Contains filter.
/// The committed value must still be one of the supplied options.
/// </summary>
public sealed class StrictSearchableComboBox : ComboBox
{
    private readonly IReadOnlyList<string> _options;
    private bool _updating;
    private string _committedValue = "";
    private int _filterVersion;

    // Avalonia styles custom control subclasses by their concrete type. Reuse the
    // native ComboBox theme so this behavioral subclass receives its visual template.
    protected override Type StyleKeyOverride => typeof(ComboBox);

    public StrictSearchableComboBox(
        IEnumerable<string> options,
        string? initialValue = null,
        bool preserveUnknownInitialValue = false)
    {
        _options = options
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IsEditable = true;
        IsTextSearchEnabled = false;
        ItemsSource = _options;

        var initialText = initialValue?.Trim() ?? "";
        var initialMatch = FindMatch(initialText);
        if (initialMatch != null)
        {
            _committedValue = initialMatch;
            SetDisplayedValue(initialMatch);
        }
        else if (preserveUnknownInitialValue && initialText.Length > 0)
        {
            _committedValue = initialText;
            SetDisplayedValue(initialText);
        }

        PropertyChanged += OnComboBoxPropertyChanged;
        SelectionChanged += OnSelectionChanged;
        DropDownOpened += OnDropDownOpened;
        DropDownClosed += OnDropDownClosed;
        LostFocus += OnLostFocus;
    }

    public IReadOnlyList<string> Options => _options;

    /// <summary>
    /// Returns the canonical option matching the current text, or an empty string
    /// when the editable text is empty or not part of the closed option set.
    /// </summary>
    public string Value
    {
        get
        {
            var input = Text?.Trim() ?? "";
            return FindMatch(input) ??
                   (input.Equals(_committedValue, StringComparison.OrdinalIgnoreCase) ? _committedValue : "");
        }
    }

    public event EventHandler? ValueCommitted;

    private string? FindMatch(string value)
        => _options.FirstOrDefault(option => option.Equals(value, StringComparison.OrdinalIgnoreCase));

    private void OnComboBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_updating || args.Property != TextProperty)
            return;

        var requestedVersion = ++_filterVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_updating || requestedVersion != _filterVersion)
                return;

            var searchText = Text ?? "";
            ApplyFilter(searchText);
            if (IsKeyboardFocusWithin)
                IsDropDownOpen = true;
        }, DispatcherPriority.Background);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_updating || SelectedItem is not string selected)
            return;

        _filterVersion++;
        CommitSelectedItem(selected);
    }

    private void OnDropDownOpened(object? sender, EventArgs args)
    {
        var currentText = Text ?? "";
        _updating = true;
        ItemsSource = _options;
        Text = currentText;
        _updating = false;
    }

    private void OnDropDownClosed(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(ValidateText, DispatcherPriority.Background);
    }

    private void OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (!IsDropDownOpen)
            ValidateText();
    }

    private void ApplyFilter(string searchText)
    {
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _options
            : _options.Where(option => option.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        _updating = true;
        ItemsSource = filtered;
        Text = searchText;
        _updating = false;
    }

    private void ValidateText()
    {
        if (_updating)
            return;

        var input = Text?.Trim() ?? "";
        if (input.Length == 0)
        {
            Commit("");
            return;
        }

        var match = FindMatch(input);
        if (match != null)
            Commit(match);
        else
            SetDisplayedValue(_committedValue);
    }

    private void Commit(string value)
    {
        var canonical = value.Length == 0 ? "" : FindMatch(value) ?? "";
        var changed = !_committedValue.Equals(canonical, StringComparison.Ordinal);
        _committedValue = canonical;
        SetDisplayedValue(canonical);
        if (changed)
            ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void CommitSelectedItem(string value)
    {
        var canonical = FindMatch(value) ?? "";
        var changed = !_committedValue.Equals(canonical, StringComparison.Ordinal);
        _committedValue = canonical;

        // Do not replace ItemsSource or SelectedItem from inside SelectionChanged.
        // Avalonia is still completing its own selection transaction at this point.
        _updating = true;
        Text = canonical;
        _updating = false;

        if (changed)
            ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void SetDisplayedValue(string value)
    {
        _updating = true;
        ItemsSource = _options;
        SelectedItem = value.Length == 0 ? null : FindMatch(value);
        Text = value;
        _updating = false;
    }
}
