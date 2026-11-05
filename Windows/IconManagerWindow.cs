using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record IconManagerItem(string Name, string FullPath, string DisplayPath, bool IsCustom)
{
    public IconAssetSource? Source { get; init; }
}

internal sealed class IconManagerWindow : SimpleWindow
{
    private readonly List<IconManagerItem> _items;
    private readonly Dictionary<string, IconAssetSource> _originalSources;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly ListBox _itemsList;
    private readonly TextBlock _footerText;
    private readonly string? _resourcesDirectory;
    private readonly Func<IconManagerItem, AssetDestination, Task<bool>>? _moveAsync;

    public IconManagerWindow(
        IEnumerable<string> originalIconPaths,
        IEnumerable<string> customIconPaths,
        string? resourcesDirectory,
        Func<IconManagerItem, AssetDestination, Task<bool>>? moveAsync = null,
        IEnumerable<IconAssetSource>? originalSources = null)
    {
        _resourcesDirectory = resourcesDirectory;
        _moveAsync = moveAsync;
        var archiveSources = (originalSources ?? []).ToDictionary(source => source.LogicalPath, StringComparer.OrdinalIgnoreCase);
        _originalSources = originalIconPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(IconSourceResolver.LogicalPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, path => archiveSources.GetValueOrDefault(path) ??
                new IconAssetSource(path, [new IconPhysicalSource(IconSourceResolver.ChangeExtension(path, ".dds"), IconFileFormat.Dds, false)]),
                StringComparer.OrdinalIgnoreCase);
        _items = _originalSources.Keys.Concat(customIconPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(IconSourceResolver.LogicalPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateResolvedItem).OfType<IconManagerItem>()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = "Manage Icons";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var shell = new ManagerListShell(
            "Search Icons...",
            ["All", "Original", "Custom"],
            "",
            addEnabled: !string.IsNullOrWhiteSpace(_resourcesDirectory),
            disabledAddToolTip: "Select or create a mod before importing icons.");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        shell.BindFiltering(RefreshList);
        shell.AddButton.Click += async (_, _) => await ImportIconAsync();
        _itemsList = ManagerListShell.CreateVirtualizedList<IconManagerItem>(CreateRow);
        shell.ReplaceItemsHost(_itemsList);
        _footerText = shell.FooterTextBlock;
        Content = shell;
        RefreshList();
    }

    private async Task ImportIconAsync()
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
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
            await ImportToDestinationAsync(sourcePath, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { await new Prompt(PromptType.Error, "Import failed", exception.Message).ShowDialog(this); }
    }

    private async Task ImportToDestinationAsync(string sourcePath, AssetDestination destination)
    {
        if (!await CatalogAvailabilityPrompt.EnsureAsync(this,
                () => CustomAssetCatalog.LoadIconPathsResult(_resourcesDirectory), "Custom icons")) return;
        await AssetCreateService.ImportPngAsync(_resourcesDirectory!, destination, sourcePath);
        RefreshItem(destination.XmlValue);
        _items.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        _searchBox.Text = Path.GetFileNameWithoutExtension(destination.XmlValue);
        _filterComboBox.SelectedItem = "Custom";
        RefreshList();
    }

    private void RefreshList()
    {
        _footerText.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => !item.IsCustom),
            _items.Count(item => item.IsCustom),
            "Double-click a custom icon to rename or move it. Paired PNG/DDS files cannot be moved.");
        _itemsList.ItemsSource = ManagerListShell.FilterItems(
            _items,
            item => item.Name,
            item => !item.IsCustom,
            _searchBox.Text,
            _filterComboBox.SelectedItem as string);
    }

    private IconManagerItem? CreateResolvedItem(string logicalPath)
    {
        var source = IconSourceResolver.Merge(logicalPath,
            (IconSourceResolver.ResolveCustom(_resourcesDirectory, logicalPath)?.PhysicalSources ?? [])
            .Concat(_originalSources.GetValueOrDefault(logicalPath)?.PhysicalSources ?? []));
        return source.PhysicalSources.Count == 0 ? null :
            new IconManagerItem(GetFileName(source.LogicalPath), source.LogicalPath, RemoveResourcesPrefix(source.LogicalPath), source.IsCustom)
            { Source = source };
    }

    private void RefreshItem(string logicalPath)
    {
        _items.RemoveAll(item => item.FullPath.Equals(logicalPath, StringComparison.OrdinalIgnoreCase));
        if (CreateResolvedItem(logicalPath) is { } current) _items.Add(current);
    }

    private string? MoveBlockReason(IconManagerItem item)
    {
        if (!item.IsCustom) return "Original game icons cannot be renamed or moved.";
        if (item.Source == null || !item.Source.IsCustom) return "The custom source is unavailable. Reopen the icon manager.";
        if (item.Source.HasCompetingCustomFiles) return "Both PNG and DDS files supply this custom icon. Resolve the competing files before renaming or moving it.";
        if (item.Source.PreferredSource?.Format is not (IconFileFormat.Png or IconFileFormat.Dds)) return "Only PNG or DDS icon files can be moved.";
        if (_moveAsync == null || string.IsNullOrWhiteSpace(_resourcesDirectory)) return "Select a writable mod before renaming or moving custom icons.";
        return null;
    }

    private Control CreateRow(IconManagerItem item)
    {
        var row = ManagerListShell.CreateSourceRow();
        row.Margin = new Thickness(0, 1, ManagerListShell.ScrollBarClearance, 1);
        row.Children.Add(new TextBlock
        {
            Text = item.Name,
            Margin = new Thickness(10, 9),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var path = new TextBlock
        {
            Text = item.IsCustom
                ? $"Custom {(item.Source?.HasCompetingCustomFiles == true ? "PNG + DDS · move blocked" : item.Source?.PreferredSource?.Format.ToString().ToUpperInvariant())} · {item.DisplayPath}"
                : item.DisplayPath,
            Margin = new Thickness(8, 9),
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 430
        };
        var sourceDetails = string.Join("\n", item.Source?.PhysicalSources.Select(source =>
            source.IsCustom ? source.Path : $"{source.ArchivePath ?? "Game archive"} :: {source.Path}") ?? []);
        ToolTip.SetTip(path, sourceDetails);
        Grid.SetColumn(path, 1);
        row.Children.Add(path);
        var blocked = MoveBlockReason(item);
        if (blocked == null)
        {
            row.DoubleTapped += async (_, _) => await OpenMoveDialogAsync(item);
            ToolTip.SetTip(row, "Double-click to rename or move this custom icon");
        }
        else ToolTip.SetTip(row, blocked);
        return row;
    }

    private async Task OpenMoveDialogAsync(IconManagerItem item)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        if (!await CatalogAvailabilityPrompt.EnsureAsync(this,
                () => CustomAssetCatalog.LoadIconPathsResult(_resourcesDirectory), "Custom icons")) return;
        if (MoveBlockReason(item) is { } reason)
        {
            await new Prompt(PromptType.Error, "Icon move blocked", reason).ShowDialog(this);
            return;
        }
        var relative = RemoveResourcesPrefix(item.FullPath);
        var extension = item.Source!.PreferredSource!.Format == IconFileFormat.Dds ? ".dds" : ".png";
        var dialog = new AssetDestinationWindow(
            "Rename/Move Icon", _resourcesDirectory!, "game\\ui_myth\\resources", "resources", extension,
            Path.GetFileNameWithoutExtension(relative), Path.GetDirectoryName(relative) ?? "", confirmButtonText: "Save", usePngIconXmlPath: true);
        await dialog.ShowDialog(this);
        if (dialog.Destination is not { } destination || !await _moveAsync!(item, destination)) return;
        RefreshItem(item.FullPath);
        RefreshItem(destination.XmlValue);
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
