using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed class AnimFileManagerWindow : SimpleWindow
{
    private readonly List<AnimFileCatalogEntry> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly StackPanel _itemsPanel;
    private readonly TextBlock _footerText;
    private readonly Func<AnimFileCatalogEntry, Task<string?>> _loadXmlAsync;
    private readonly string? _customArtDirectory;
    private readonly Func<AnimFileCatalogEntry, AssetDestination, Task<bool>>? _moveAsync;
    private readonly DispatcherTimer _searchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    public AnimFileManagerWindow(
        IEnumerable<AnimFileCatalogEntry> animFiles,
        Func<AnimFileCatalogEntry, Task<string?>> loadXmlAsync,
        string? customArtDirectory,
        Func<AnimFileCatalogEntry, AssetDestination, Task<bool>>? moveAsync = null)
    {
        _loadXmlAsync = loadXmlAsync ?? throw new ArgumentNullException(nameof(loadXmlAsync));
        _customArtDirectory = customArtDirectory;
        _moveAsync = moveAsync;
        _items = animFiles
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = "Manage Anim Files";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414");
        Foreground = Brush.Parse("#d9d9d9");

        var shell = new ManagerListShell(
            "Search Anim Files (type at least 3 characters)...",
            ["All", "Original", "Custom"],
            "",
            addEnabled: false,
            disabledAddToolTip: "Adding custom animation files is not available yet.");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        _itemsPanel = shell.ItemsPanel;
        _footerText = shell.FooterTextBlock;
        _searchBox.TextChanged += (_, _) => ScheduleRefresh();
        _filterComboBox.SelectionChanged += (_, _) => RefreshList();
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RefreshList();
        };
        Closed += (_, _) => _searchDebounceTimer.Stop();
        Content = shell;
        RefreshList();
    }

    private void ScheduleRefresh()
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RefreshList()
    {
        _footerText.Text = $"{_items.Count:N0} items: {_items.Count(item => !item.IsCustom):N0} original, {_items.Count(item => item.IsCustom):N0} customs. Double-click a custom animation file name to rename or move it.";
        _itemsPanel.Children.Clear();
        var search = _searchBox.Text?.Trim() ?? "";
        var sourceFilter = _filterComboBox.SelectedItem as string ?? "All";
        if (search.Length < 3)
        {
            _itemsPanel.Children.Add(new TextBlock
            {
                Text = "Type at least 3 characters to display animation files.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 9)
            });
            return;
        }

        foreach (var item in _items
                     .Where(item => sourceFilter == "All" ||
                                    (sourceFilter == "Original" && !item.IsCustom) ||
                                    (sourceFilter == "Custom" && item.IsCustom))
                     .Where(item => GetFileName(item.Path).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    item.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    item.ArchiveName.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var row = ManagerListShell.CreateRow("*,Auto,Auto,Auto");
            row.Children.Add(new TextBlock
            {
                Text = GetFileName(item.Path),
                Margin = new Thickness(10, 9),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var source = new TextBlock
            {
                Text = item.ArchiveName,
                Foreground = Brushes.Gray,
                Margin = new Thickness(8, 9),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(source, 1);
            row.Children.Add(source);

            var editButton = CreateEditButton(item);
            Grid.SetColumn(editButton, 2);
            row.Children.Add(editButton);

            var duplicateButton = CreateDuplicateButton(item);
            Grid.SetColumn(duplicateButton, 3);
            row.Children.Add(duplicateButton);
            if (item.IsCustom && _moveAsync != null && !string.IsNullOrWhiteSpace(_customArtDirectory))
            {
                row.DoubleTapped += async (_, eventArgs) =>
                {
                    if (eventArgs.Source is Control source &&
                        (source is Button || source.GetVisualAncestors().OfType<Button>().Any()))
                        return;
                    await OpenMoveDialogAsync(item);
                };
                ToolTip.SetTip(row, "Double-click to rename or move this custom animation file");
            }
            _itemsPanel.Children.Add(row);
        }
    }

    private async Task OpenMoveDialogAsync(AnimFileCatalogEntry item)
    {
        var dialog = new AssetDestinationWindow(
            "Rename/Move Anim File", _customArtDirectory!, "game\\art", "", ".xml",
            Path.GetFileNameWithoutExtension(item.Path), Path.GetDirectoryName(item.Path) ?? "", confirmButtonText: "Save");
        await dialog.ShowDialog(this);
        if (dialog.Destination is not { } destination || !await _moveAsync!(item, destination)) return;
        _items.Remove(item);
        _items.Add(new AnimFileCatalogEntry(destination.XmlValue, "Custom", IsCustom: true));
        _items.Sort((left, right) =>
        {
            var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(GetFileName(left.Path), GetFileName(right.Path));
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
        });
        _searchBox.Text = Path.GetFileNameWithoutExtension(destination.XmlValue);
        _filterComboBox.SelectedItem = "Custom";
        RefreshList();
    }

    private Button CreateEditButton(AnimFileCatalogEntry item)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = "✎",
                FontSize = 16,
                RenderTransform = new ScaleTransform(-1, 1),
                RenderTransformOrigin = RelativePoint.Center
            },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.IsEnabled = !string.IsNullOrWhiteSpace(_customArtDirectory);
        ToolTip.SetTip(button, "View animation XML");
        button.Click += async (_, _) => await OpenViewerAsync(item, button);
        return button;
    }

    private async Task OpenViewerAsync(AnimFileCatalogEntry item, Button button)
    {
        button.IsEnabled = false;
        try
        {
            var xml = await _loadXmlAsync(item);
            if (string.IsNullOrWhiteSpace(xml))
            {
                await new Prompt(
                    PromptType.Error,
                    "Animation file unavailable",
                    $"{GetFileName(item.Path)} could not be read from {item.ArchiveName}.").ShowDialog(this);
                return;
            }

            var viewer = new AnimFileViewerWindow(
                GetFileName(item.Path),
                XmlPreviewFormatter.Beautify(xml));
            await viewer.ShowDialog(this);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private Button CreateDuplicateButton(AnimFileCatalogEntry item)
    {
        var duplicateIcon = new Canvas { Width = 16, Height = 16 };
        var backPage = new Border
        {
            Width = 10,
            Height = 12,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(1)
        };
        Canvas.SetLeft(backPage, 5);
        Canvas.SetTop(backPage, 1);
        duplicateIcon.Children.Add(backPage);
        var frontPage = new Border
        {
            Width = 10,
            Height = 12,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(1),
            Background = Brush.Parse("#202020")
        };
        Canvas.SetLeft(frontPage, 1);
        Canvas.SetTop(frontPage, 4);
        duplicateIcon.Children.Add(frontPage);

        var button = new Button
        {
            Content = duplicateIcon,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, "Duplicate animation file into this mod");
        button.Click += async (_, _) => await DuplicateAsync(item, button);
        return button;
    }

    private async Task DuplicateAsync(AnimFileCatalogEntry item, Button button)
    {
        if (string.IsNullOrWhiteSpace(_customArtDirectory)) return;
        button.IsEnabled = false;
        try
        {
            var xml = await _loadXmlAsync(item);
            if (string.IsNullOrWhiteSpace(xml))
            { await new Prompt(PromptType.Error, "Animation file unavailable", $"{GetFileName(item.Path)} could not be read.").ShowDialog(this); return; }
            var defaultName = Path.GetFileNameWithoutExtension(GetFileName(item.Path)) + "_copy";
            var dialog = new AssetDestinationWindow("Duplicate Anim File", _customArtDirectory, "game\\art", "", ".xml", defaultName);
            await dialog.ShowDialog(this);
            if (dialog.Destination is not { } destination) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination.AbsolutePath)!);
            await File.WriteAllTextAsync(destination.AbsolutePath, xml);
            _items.RemoveAll(entry => entry.Path.Equals(destination.XmlValue, StringComparison.OrdinalIgnoreCase));
            _items.Add(new AnimFileCatalogEntry(destination.XmlValue, "Custom", IsCustom: true));
            _items.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(GetFileName(left.Path), GetFileName(right.Path)));
            _searchBox.Text = Path.GetFileNameWithoutExtension(destination.XmlValue);
            _filterComboBox.SelectedItem = "Custom";
            RefreshList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { await new Prompt(PromptType.Error, "Duplication failed", exception.Message).ShowDialog(this); }
        finally { button.IsEnabled = true; }
    }

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('/', '\\');
        var separator = normalized.LastIndexOf('\\');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }
}
