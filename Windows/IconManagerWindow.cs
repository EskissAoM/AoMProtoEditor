using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record IconManagerItem(string Name, string FullPath, string DisplayPath, bool IsCustom);

internal sealed class IconManagerWindow : SimpleWindow
{
    private readonly List<IconManagerItem> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly StackPanel _itemsPanel;
    private readonly TextBlock _footerText;
    private readonly string? _resourcesDirectory;
    private readonly Func<IconManagerItem, AssetDestination, Task<bool>>? _moveAsync;
    private readonly DispatcherTimer _searchDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    public IconManagerWindow(
        IEnumerable<string> originalIconPaths,
        IEnumerable<string> customIconPaths,
        string? resourcesDirectory,
        Func<IconManagerItem, AssetDestination, Task<bool>>? moveAsync = null)
    {
        _resourcesDirectory = resourcesDirectory;
        _moveAsync = moveAsync;
        var originals = originalIconPaths.Select(path => (Path: path, IsCustom: false));
        var customs = customIconPaths.Select(path => (Path: path, IsCustom: true));
        _items = originals.Concat(customs)
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.IsCustom).First())
            .Select(item => new IconManagerItem(
                GetFileName(item.Path),
                item.Path,
                RemoveResourcesPrefix(item.Path),
                item.IsCustom))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = "Manage Icons";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414");
        Foreground = Brush.Parse("#d9d9d9");

        var shell = new ManagerListShell(
            "Search Icons (type at least 3 characters)...",
            ["All", "Original", "Custom"],
            "",
            addEnabled: !string.IsNullOrWhiteSpace(_resourcesDirectory),
            disabledAddToolTip: "Select or create a mod before importing icons.");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        _searchBox.TextChanged += (_, _) => ScheduleRefresh();
        _filterComboBox.SelectionChanged += (_, _) => RefreshList();
        shell.AddButton.Click += async (_, _) => await ImportIconAsync();
        _itemsPanel = shell.ItemsPanel;
        _footerText = shell.FooterTextBlock;
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RefreshList();
        };
        Closed += (_, _) => _searchDebounceTimer.Stop();
        Content = shell;
        RefreshList();
    }

    private async Task ImportIconAsync()
    {
        if (string.IsNullOrWhiteSpace(_resourcesDirectory)) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select PNG Icon",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG Images") { Patterns = ["*.png"] }]
        });
        if (files.Count == 0) return;
        var sourcePath = files[0].Path.LocalPath;
        if (!Path.GetExtension(sourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
        { await new Prompt(PromptType.Error, "Invalid icon", "Select an existing PNG image.").ShowDialog(this); return; }

        var destinationDialog = new AssetDestinationWindow(
            "Import Icon",
            _resourcesDirectory,
            "game\\ui_myth\\resources",
            "resources",
            ".png",
            Path.GetFileNameWithoutExtension(sourcePath));
        await destinationDialog.ShowDialog(this);
        if (destinationDialog.Destination is not { } destination) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination.AbsolutePath)!);
            File.Copy(sourcePath, destination.AbsolutePath, overwrite: false);
            _items.RemoveAll(item => item.FullPath.Equals(destination.XmlValue, StringComparison.OrdinalIgnoreCase));
            _items.Add(new IconManagerItem(GetFileName(destination.XmlValue), destination.XmlValue, RemoveResourcesPrefix(destination.XmlValue), true));
            _items.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            _searchBox.Text = Path.GetFileNameWithoutExtension(destination.XmlValue);
            _filterComboBox.SelectedItem = "Custom";
            RefreshList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { await new Prompt(PromptType.Error, "Import failed", exception.Message).ShowDialog(this); }
    }

    private void ScheduleRefresh()
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RefreshList()
    {
        _footerText.Text = $"{_items.Count:N0} items: {_items.Count(item => !item.IsCustom):N0} original, {_items.Count(item => item.IsCustom):N0} customs. Double-click a custom icon name to rename or move it.";
        _itemsPanel.Children.Clear();
        var search = _searchBox.Text?.Trim() ?? "";
        var sourceFilter = _filterComboBox.SelectedItem as string ?? "All";
        if (search.Length < 3)
        {
            _itemsPanel.Children.Add(new TextBlock
            {
                Text = "Type at least 3 characters to display icons.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10, 9)
            });
            return;
        }

        foreach (var item in _items
                     .Where(item => sourceFilter == "All" ||
                                    (sourceFilter == "Original" && !item.IsCustom) ||
                                    (sourceFilter == "Custom" && item.IsCustom))
                     .Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    item.DisplayPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    (item.IsCustom ? "Custom" : "UITextureCache.bar")
                                    .Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var row = ManagerListShell.CreateRow("*,Auto");
            row.Children.Add(new TextBlock
            {
                Text = item.Name,
                Margin = new Thickness(10, 9),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var path = new TextBlock
            {
                Text = item.IsCustom ? $"Custom · {item.DisplayPath}" : item.DisplayPath,
                Margin = new Thickness(8, 9),
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 430
            };
            ToolTip.SetTip(path, item.DisplayPath);
            Grid.SetColumn(path, 1);
            row.Children.Add(path);
            if (item.IsCustom && _moveAsync != null && !string.IsNullOrWhiteSpace(_resourcesDirectory))
            {
                row.DoubleTapped += async (_, _) => await OpenMoveDialogAsync(item);
                ToolTip.SetTip(row, "Double-click to rename or move this custom icon");
            }
            _itemsPanel.Children.Add(row);
        }
    }

    private async Task OpenMoveDialogAsync(IconManagerItem item)
    {
        var relative = RemoveResourcesPrefix(item.FullPath);
        var dialog = new AssetDestinationWindow(
            "Rename/Move Icon", _resourcesDirectory!, "game\\ui_myth\\resources", "resources", ".png",
            Path.GetFileNameWithoutExtension(relative), Path.GetDirectoryName(relative) ?? "", confirmButtonText: "Save");
        await dialog.ShowDialog(this);
        if (dialog.Destination is not { } destination || !await _moveAsync!(item, destination)) return;
        _items.Remove(item);
        _items.Add(new IconManagerItem(GetFileName(destination.XmlValue), destination.XmlValue, RemoveResourcesPrefix(destination.XmlValue), true));
        _items.Sort((left, right) =>
        {
            var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.FullPath, right.FullPath);
        });
        _searchBox.Text = Path.GetFileNameWithoutExtension(destination.XmlValue);
        _filterComboBox.SelectedItem = "Custom";
        RefreshList();
    }

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('/', '\\');
        var separator = normalized.LastIndexOf('\\');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    internal static string RemoveResourcesPrefix(string path)
    {
        var normalized = path.Replace('/', '\\');
        const string prefix = "resources\\";
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[prefix.Length..]
            : normalized;
    }
}
