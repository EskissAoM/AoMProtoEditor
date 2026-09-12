using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

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
    private readonly Grid _root;
    private readonly Control _itemsHost;
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
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
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
        Grid.SetRow(_itemsHost, 1);
        _root.Children.Add(_itemsHost);

        FooterTextBlock = new TextBlock
        {
            Text = footerText,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(FooterTextBlock, 2);
        _root.Children.Add(FooterTextBlock);
        Content = _root;
    }

    public void ReplaceItemsHost(Control host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _root.Children.Remove(_itemsHost);
        Grid.SetRow(host, 1);
        _root.Children.Add(host);
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

    public static string FormatEntityCountFooter(int total, int original, int custom, string hint)
        => $"{total:N0} items: {original:N0} original, {custom:N0} customs. {hint}";
}
