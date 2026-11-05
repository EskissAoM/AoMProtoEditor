using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Data;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>Shared manager chrome used by catalog/definition managers.</summary>
internal sealed class ManagerListShell : UserControl
{
    public const double ScrollBarClearance = 20;
    public const double HeaderControlHeight = 36;

    public TextBox SearchBox { get; }
    public ComboBox FilterComboBox { get; }
    public Button AddButton { get; }
    public StackPanel ItemsPanel { get; }
    public TextBlock FooterTextBlock { get; }
    internal TextBlock EmptyStateMessage { get; }
    private readonly Grid _root;
    private readonly Grid _itemsArea;
    private Control _itemsHost;
    private ListBox? _itemsList;
    private readonly EditorSaveFeedback _feedback;
    private int _activities;
    private DispatcherTimer? _searchDebounceTimer;

    public ManagerListShell(
        string searchPlaceholder,
        IEnumerable<string> filters,
        string footerText,
        bool addEnabled = true,
        string? disabledAddToolTip = null)
    {
        _root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto")
        };

        var topPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,120,Auto"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        SearchBox = new TextBox
        {
            PlaceholderText = searchPlaceholder,
            Height = HeaderControlHeight,
            Margin = new Thickness(0, 0, 8, 0)
        };
        topPanel.Children.Add(SearchBox);

        FilterComboBox = new ComboBox
        {
            ItemsSource = filters.ToList(),
            SelectedIndex = 0,
            Height = HeaderControlHeight,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(FilterComboBox, 1);
        topPanel.Children.Add(FilterComboBox);

        AddButton = new Button
        {
            Content = "+",
            FontSize = 22,
            Width = 40,
            Height = HeaderControlHeight,
            Padding = new Thickness(0),
            Classes = { "add-item" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = addEnabled
        };
        if (!string.IsNullOrWhiteSpace(disabledAddToolTip))
            ToolTip.SetTip(AddButton, disabledAddToolTip);
        Grid.SetColumn(AddButton, 2);
        topPanel.Children.Add(AddButton);
        _root.Children.Add(topPanel);

        ItemsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, ScrollBarClearance, 0) };
        _itemsHost = new ScrollViewer
        {
            Content = ItemsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        EmptyStateMessage = new TextBlock
        {
            Foreground = Brush.Parse("#B6B0A4"), TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16), IsHitTestVisible = false, IsVisible = false
        };
        _itemsArea = new Grid { Children = { _itemsHost, EmptyStateMessage } };
        Grid.SetRow(_itemsArea, 1);
        _root.Children.Add(_itemsArea);

        FooterTextBlock = new TextBlock
        {
            Text = footerText,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(FooterTextBlock, 2);
        _root.Children.Add(FooterTextBlock);
        var saveStatus = new TextBlock { MinHeight = 20, TextAlignment = TextAlignment.Center };
        Grid.SetRow(saveStatus, 3);
        _root.Children.Add(saveStatus);
        _feedback = new EditorSaveFeedback(saveStatus);
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner) _feedback.Register(owner);
        };
        Content = _root;
    }

    public void ReplaceItemsHost(Control host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_itemsList != null) _itemsList.PropertyChanged -= ListChanged;
        _itemsArea.Children.Remove(_itemsHost);
        _itemsHost = host;
        _itemsArea.Children.Insert(0, host);
        _itemsList = host as ListBox;
        if (_itemsList != null) _itemsList.PropertyChanged += ListChanged;
        UpdateEmptyState();
    }

    private void ListChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ItemsControl.ItemCountProperty || args.Property == ItemsControl.ItemsSourceProperty)
            UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateMessage.IsVisible = _itemsList?.ItemCount == 0 && _activities == 0;
        EmptyStateMessage.Text = !string.IsNullOrWhiteSpace(SearchBox.Text) ? "No matching entries."
            : FilterComboBox.SelectedItem is string filter && filter != "All" ? "No entries in this filter."
            : "No entries available.";
    }

    // Presentation only: existing owners keep their command gates, callbacks and
    // enabled-state restoration. Overlapping permitted operations share feedback.
    internal IDisposable BeginActivity()
    {
        _activities++;
        _feedback.ShowContextMessage("Working…");
        UpdateEmptyState();
        return new Activity(() =>
        {
            if (--_activities == 0) _feedback.ClearContextMessage();
            UpdateEmptyState();
        });
    }

    private sealed class Activity(Action finish) : IDisposable
    {
        private Action? _finish = finish;
        public void Dispose() => Interlocked.Exchange(ref _finish, null)?.Invoke();
    }

    public void BindFiltering(Action refresh, int searchDebounceMilliseconds = 175)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(searchDebounceMilliseconds)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            refresh();
        };
        SearchBox.TextChanged += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        };
        FilterComboBox.SelectionChanged += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            refresh();
        };
        DetachedFromVisualTree += (_, _) => _searchDebounceTimer.Stop();
    }

    public static ListBox CreateVirtualizedList<T>(Func<T, Control> createRow)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(createRow);
        return new ListBox
        {
            Classes = { "manager-list" },
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 0.5 }),
            // During a fast recycle pass Avalonia can briefly rebuild a cleared
            // container with null content. Never pass that transient value to a
            // manager row factory: those factories require a real catalog item.
            ItemTemplate = new FuncDataTemplate<T>(
                (item, _) => item is null
                    ? new Border { IsVisible = false, IsHitTestVisible = false }
                    : createRow(item),
                supportsRecycling: false)
        };
    }

    public static Grid CreateRow(string columns)
        => new()
        {
            ColumnDefinitions = new ColumnDefinitions(columns),
            Background = Brush.Parse("#191C1A"),
            Margin = new Thickness(0, 1)
        };

    public static Grid CreateActionRow(int actionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionCount);
        // 28px buttons plus their two existing 4px margins. Reserve unavailable
        // slots too, without exposing a new operation on original/read-only rows.
        return CreateDescribedRow("*,Auto," + string.Join(',', Enumerable.Repeat("36", actionCount)), 160);
    }

    public static Grid CreateSourceRow() => CreateDescribedRow("2*,3*", double.PositiveInfinity);

    private static Grid CreateDescribedRow(string columns, double statusWidth)
    {
        var row = CreateRow(columns);
        row.Children.CollectionChanged += (_, args) =>
        {
            if (args.NewItems == null) return;
            foreach (var text in args.NewItems.OfType<TextBlock>())
            {
                if (Grid.GetColumn(text) > 1) continue;
                if (Grid.GetColumn(text) == 1)
                {
                    text.MaxWidth = Math.Min(text.MaxWidth, statusWidth);
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                }
                // Preserve specialized source/read-only explanations. Otherwise
                // expose the complete current text even when the row is clipped.
                if (ToolTip.GetTip(text) == null)
                    text.Bind(ToolTip.TipProperty, new Binding(nameof(TextBlock.Text)) { Source = text });
            }
        };
        return row;
    }

    public static IReadOnlyList<T> FilterItems<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector,
        Func<T, bool> isOriginalSelector,
        string? search,
        string? sourceFilter)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameSelector);
        ArgumentNullException.ThrowIfNull(isOriginalSelector);
        var query = search?.Trim() ?? "";
        var filter = sourceFilter ?? "All";
        return items
            .Where(item => filter == "All" ||
                           filter == "Original" && isOriginalSelector(item) ||
                           filter == "Custom" && !isOriginalSelector(item))
            .Where(item => query.Length == 0 ||
                           nameSelector(item).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string FormatTypeUsageStatus(bool isBuiltIn, int usageCount)
        => isBuiltIn
            ? usageCount > 0 ? $"Data.bar · Used by {usageCount}" : "Data.bar"
            : usageCount > 0 ? $"Used by {usageCount}" : "Custom";

    public static string FormatEntityCountFooter(int total, int original, int custom, string hint)
        => $"{total:N0} {(total == 1 ? "item" : "items")}: {original:N0} original, {custom:N0} custom. {hint}";
}
