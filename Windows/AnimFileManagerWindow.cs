using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record XmlAssetManagerProfile(
    string WindowTitle,
    string SearchLabel,
    string ItemLabel,
    string ItemPlural,
    string ViewerLabel,
    string RootDisplay,
    string MoveTitle,
    string DuplicateTitle,
    string UnavailableTitle)
{
    public static XmlAssetManagerProfile AnimFile { get; } = new(
        "Manage Anim Files", "Anim Files", "animation file", "animation files", "Anim File",
        "game\\art", "Rename/Move Anim File", "Duplicate Anim File", "Animation file unavailable");
    public static XmlAssetManagerProfile SoundSet { get; } = new(
        "Manage Sound Sets", "Sound Sets", "sound set", "sound sets", "Sound Set",
        "game\\sound", "Rename/Move Sound Set", "Duplicate Sound Set", "Sound set unavailable");
}

internal sealed class AnimFileManagerWindow : SimpleWindow
{
    private readonly List<AnimFileCatalogEntry> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly ListBox _itemsList;
    private readonly TextBlock _footerText;
    private readonly Func<AnimFileCatalogEntry, Task<string?>> _loadXmlAsync;
    private readonly Func<AnimFileCatalogEntry, Task<AssetPayloadResult<string>>>? _loadXmlResultAsync;
    private readonly string? _customArtDirectory;
    private readonly Func<AnimFileCatalogEntry, AssetDestination, Task<bool>>? _moveAsync;
    private readonly XmlAssetManagerProfile _profile;
    internal bool HasSavedXml { get; private set; }

    public AnimFileManagerWindow(
        IEnumerable<AnimFileCatalogEntry> animFiles,
        Func<AnimFileCatalogEntry, Task<string?>> loadXmlAsync,
        string? customArtDirectory,
        Func<AnimFileCatalogEntry, AssetDestination, Task<bool>>? moveAsync = null,
        XmlAssetManagerProfile? profile = null,
        Func<AnimFileCatalogEntry, Task<AssetPayloadResult<string>>>? loadXmlResultAsync = null)
    {
        _profile = profile ?? XmlAssetManagerProfile.AnimFile;
        _loadXmlAsync = loadXmlAsync ?? throw new ArgumentNullException(nameof(loadXmlAsync));
        _loadXmlResultAsync = loadXmlResultAsync;
        _customArtDirectory = customArtDirectory;
        _moveAsync = moveAsync;
        _items = animFiles
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Title = _profile.WindowTitle;
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var shell = new ManagerListShell(
            $"Search {_profile.SearchLabel}...",
            ["All", "Original", "Custom"],
            "",
            addEnabled: false,
            disabledAddToolTip: $"Adding custom {_profile.ItemPlural} is not available yet.");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        _itemsList = ManagerListShell.CreateVirtualizedList<AnimFileCatalogEntry>(CreateRow);
        shell.ReplaceItemsHost(_itemsList);
        _footerText = shell.FooterTextBlock;
        shell.BindFiltering(RefreshList);
        Content = shell;
        RefreshList();
    }

    private void RefreshList()
    {
        _footerText.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => !item.IsCustom),
            _items.Count(item => item.IsCustom),
            $"Double-click a custom {_profile.ItemLabel} name to rename or move it.");
        _itemsList.ItemsSource = ManagerListShell.FilterItems(
            _items,
            item => GetFileName(item.Path),
            item => !item.IsCustom,
            _searchBox.Text,
            _filterComboBox.SelectedItem as string);
    }

    private Control CreateRow(AnimFileCatalogEntry item)
    {
        var row = ManagerListShell.CreateActionRow(2);
        row.Margin = new Thickness(0, 1, ManagerListShell.ScrollBarClearance, 1);
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
                if (eventArgs.Source is Control eventSource &&
                    (eventSource is Button || eventSource.GetVisualAncestors().OfType<Button>().Any()))
                    return;
                await OpenMoveDialogAsync(item);
            };
            ToolTip.SetTip(row, $"Double-click to rename or move this custom {_profile.ItemLabel}");
        }
        return row;
    }

    private async Task OpenMoveDialogAsync(AnimFileCatalogEntry item)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        if (!await CatalogAvailabilityPrompt.EnsureAsync(this,
                () => CustomAssetCatalog.LoadAnimFilesResult(_customArtDirectory), _profile.SearchLabel)) return;
        var dialog = new AssetDestinationWindow(
            _profile.MoveTitle, _customArtDirectory!, _profile.RootDisplay, "", ".xml",
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
        var button = ManagerActionButtons.Edit($"View{(item.IsCustom ? "/edit" : "")} {_profile.ItemLabel} XML");
        button.Click += async (_, _) => await OpenViewerAsync(item, button);
        return button;
    }

    private async Task OpenViewerAsync(AnimFileCatalogEntry item, Button button)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        button.IsEnabled = false;
        try
        {
            var load = await LoadXmlResultAsync(item);
            var xml = load.Value;
            if (string.IsNullOrWhiteSpace(xml))
            {
                await new Prompt(
                    PromptType.Error,
                    _profile.UnavailableTitle,
                    $"{load.Source}\n\n{load.Error}").FitToContent(600, 360).ShowDialog(this);
                return;
            }

            var source = item.IsCustom && !string.IsNullOrWhiteSpace(_customArtDirectory)
                ? await XmlAssetEditingSource.OpenAsync(_customArtDirectory, item.Path) : null;
            var viewer = new AnimFileViewerWindow(
                GetFileName(item.Path),
                XmlPreviewFormatter.Beautify(xml),
                _profile.ViewerLabel, source);
            viewer.Saved += (_, _) => HasSavedXml = true;
            await viewer.ShowDialog(this);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        {
            await new Prompt(PromptType.Error, _profile.UnavailableTitle, exception.Message).FitToContent(600, 360).ShowDialog(this);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task<AssetPayloadResult<string>> LoadXmlResultAsync(AnimFileCatalogEntry item)
    {
        if (_loadXmlResultAsync != null) return await _loadXmlResultAsync(item);
        var xml = await _loadXmlAsync(item);
        return string.IsNullOrWhiteSpace(xml)
            ? AssetPayloadResult<string>.Failure(item.Path, $"XML could not be read from {item.ArchiveName}.")
            : AssetPayloadResult<string>.Success(xml, item.Path);
    }

    private Button CreateDuplicateButton(AnimFileCatalogEntry item)
    {
        var button = ManagerActionButtons.Copy($"Duplicate {_profile.ItemLabel} into this mod");
        button.Click += async (_, _) => await DuplicateAsync(item, button);
        return button;
    }

    private async Task DuplicateAsync(AnimFileCatalogEntry item, Button button)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        if (string.IsNullOrWhiteSpace(_customArtDirectory)) return;
        button.IsEnabled = false;
        try
        {
            var load = await LoadXmlResultAsync(item);
            var xml = load.Value;
            if (string.IsNullOrWhiteSpace(xml))
            { await new Prompt(PromptType.Error, _profile.UnavailableTitle, $"{load.Source}\n\n{load.Error}").FitToContent(600, 360).ShowDialog(this); return; }
            var defaultName = Path.GetFileNameWithoutExtension(GetFileName(item.Path)) + "_copy";
            var dialog = new AssetDestinationWindow(_profile.DuplicateTitle, _customArtDirectory, _profile.RootDisplay, "", ".xml", defaultName);
            await dialog.ShowDialog(this);
            if (dialog.Destination is not { } destination) return;
            await DuplicateToDestinationAsync(xml, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { await new Prompt(PromptType.Error, "Duplication failed", exception.Message).ShowDialog(this); }
        finally { button.IsEnabled = true; }
    }

    private async Task DuplicateToDestinationAsync(string xml, AssetDestination destination)
    {
        if (!await CatalogAvailabilityPrompt.EnsureAsync(this,
                () => CustomAssetCatalog.LoadAnimFilesResult(_customArtDirectory), _profile.SearchLabel)) return;
        await AssetCreateService.CreateXmlAsync(_customArtDirectory!, destination, xml);
        _items.RemoveAll(entry => entry.Path.Equals(destination.XmlValue, StringComparison.OrdinalIgnoreCase));
        _items.Add(new AnimFileCatalogEntry(destination.XmlValue, "Custom", IsCustom: true));
        _items.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(GetFileName(left.Path), GetFileName(right.Path)));
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
}
