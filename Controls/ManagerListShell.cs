using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Controls;

/// <summary>Shared manager chrome used by catalog/definition managers.</summary>
internal sealed class ManagerListShell : UserControl
{
    public TextBox SearchBox { get; }
    public ComboBox FilterComboBox { get; }
    public Button AddButton { get; }
    public StackPanel ItemsPanel { get; }
    public TextBlock FooterTextBlock { get; }

    public ManagerListShell(
        string searchPlaceholder,
        IEnumerable<string> filters,
        string footerText,
        bool addEnabled = true,
        string? disabledAddToolTip = null)
    {
        var root = new Grid
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
            Margin = new Thickness(0, 0, 8, 0)
        };
        topPanel.Children.Add(SearchBox);

        FilterComboBox = new ComboBox
        {
            ItemsSource = filters.ToList(),
            SelectedIndex = 0,
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
            Height = 36,
            Padding = new Thickness(0),
            Background = Brush.Parse("#2b7a0b"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = addEnabled
        };
        if (!string.IsNullOrWhiteSpace(disabledAddToolTip))
            ToolTip.SetTip(AddButton, disabledAddToolTip);
        Grid.SetColumn(AddButton, 2);
        topPanel.Children.Add(AddButton);
        root.Children.Add(topPanel);

        ItemsPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 10, 0) };
        var scroll = new ScrollViewer
        {
            Content = ItemsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        FooterTextBlock = new TextBlock
        {
            Text = footerText,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(FooterTextBlock, 2);
        root.Children.Add(FooterTextBlock);
        Content = root;
    }

    public static Grid CreateRow(string columns)
        => new()
        {
            ColumnDefinitions = new ColumnDefinitions(columns),
            Background = Brush.Parse("#202020"),
            Margin = new Thickness(0, 1)
        };
}
