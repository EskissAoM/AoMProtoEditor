using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record CultureManagerItem(string Name, bool IsBuiltIn, int UsageCount);

internal sealed class CultureManagerWindow : SimpleWindow
{
    private sealed class EditableItem
    {
        public required string Name { get; set; }
        public required bool IsBuiltIn { get; init; }
        public required int UsageCount { get; set; }
    }

    private readonly List<EditableItem> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly ListBox _itemsList;
    private readonly TextBlock _footerText;
    private readonly Func<Window, string, bool, Task> _openEditorAsync;
    private readonly Func<string, Task<bool>> _createAsync;
    private readonly Func<string, bool, string, Task<bool>> _duplicateAsync;
    private readonly Func<string, string, Task<bool>> _renameAsync;
    private readonly Func<string, Task<bool>> _deleteAsync;
    private readonly Func<string, int> _resolveUsageCount;
    private readonly Func<Window, string, Task<bool>> _validateDeleteAsync;
    private bool _operationInProgress;

    public CultureManagerWindow(
        IEnumerable<CultureManagerItem> items,
        Func<Window, string, bool, Task> openEditorAsync,
        Func<string, Task<bool>> createAsync,
        Func<string, bool, string, Task<bool>> duplicateAsync,
        Func<string, string, Task<bool>> renameAsync,
        Func<string, Task<bool>> deleteAsync,
        Func<string, int> resolveUsageCount,
        Func<Window, string, Task<bool>> validateDeleteAsync)
    {
        _openEditorAsync = openEditorAsync;
        _createAsync = createAsync;
        _duplicateAsync = duplicateAsync;
        _renameAsync = renameAsync;
        _deleteAsync = deleteAsync;
        _resolveUsageCount = resolveUsageCount;
        _validateDeleteAsync = validateDeleteAsync;
        _items = items.Select(item => new EditableItem
        {
            Name = item.Name,
            IsBuiltIn = item.IsBuiltIn,
            UsageCount = item.UsageCount
        }).ToList();

        Title = "Manage Cultures";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var shell = new ManagerListShell(
            "Search Cultures...",
            ["All", "Original", "Custom"],
            "Double-click a custom Culture name to rename it.");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        _footerText = shell.FooterTextBlock;
        _itemsList = ManagerListShell.CreateVirtualizedList<EditableItem>(CreateRow);
        shell.ReplaceItemsHost(_itemsList);
        shell.BindFiltering(RefreshList);
        shell.AddButton.Click += async (_, _) => await RunExclusiveAsync(AddAsync);
        Content = shell;
        RefreshList();
    }

    private bool IsValidName(string name, EditableItem? ignored = null)
        => CultureCatalog.IsValidName(name) &&
           !_items.Any(item => !ReferenceEquals(item, ignored) &&
               item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task AddAsync()
    {
        var name = await PromptForNameAsync("New Culture name:");
        if (name == null || !await _createAsync(name)) return;
        var item = new EditableItem { Name = name, IsBuiltIn = false, UsageCount = 0 };
        _items.Add(item);
        RefreshList();
        await _openEditorAsync(this, item.Name, false);
        item.UsageCount = _resolveUsageCount(item.Name);
        RefreshList();
    }

    private async Task DuplicateAsync(EditableItem source)
    {
        var name = await PromptForNameAsync("Copy Culture as:");
        if (name == null || !await _duplicateAsync(source.Name, source.IsBuiltIn, name)) return;
        var item = new EditableItem { Name = name, IsBuiltIn = false, UsageCount = 0 };
        _items.Add(item);
        RefreshList();
        await _openEditorAsync(this, item.Name, false);
        RefreshList();
    }

    private async Task RenameAsync(EditableItem item)
    {
        if (item.IsBuiltIn) return;
        var prompt = new InputPromptWindow("Rename Culture:", item.Name, confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) return;
        if (!IsValidName(name, item))
        {
            await ShowInvalidNameAsync();
            return;
        }
        if (!await _renameAsync(item.Name, name)) return;
        item.Name = name;
        item.UsageCount = _resolveUsageCount(name);
        RefreshList();
    }

    private async Task DeleteAsync(EditableItem item)
    {
        if (item.IsBuiltIn) return;
        item.UsageCount = _resolveUsageCount(item.Name);
        if (!await _validateDeleteAsync(this, item.Name)) return;
        var confirm = new Prompt(
            PromptType.Confirm,
            "Remove Culture?",
            $"Are you sure you want to remove '{item.Name}'?",
            confirmButtonText: "Remove");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed || !await _deleteAsync(item.Name)) return;
        _items.Remove(item);
        RefreshList();
    }

    private async Task<string?> PromptForNameAsync(string title)
    {
        var prompt = new InputPromptWindow(title, confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return null;
        var name = prompt.InputText.Trim();
        if (IsValidName(name)) return name;
        await ShowInvalidNameAsync();
        return null;
    }

    private async Task ShowInvalidNameAsync()
        => await new Prompt(PromptType.Error, "Invalid Culture name",
            "Use a unique name containing only letters, digits, '_' or '-'.").ShowDialog(this);

    private void RefreshList()
    {
        _footerText.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => item.IsBuiltIn),
            _items.Count(item => !item.IsBuiltIn),
            "Double-click a custom Culture name to rename it.");
        _itemsList.ItemsSource = ManagerListShell.FilterItems(
            _items,
            item => item.Name,
            item => item.IsBuiltIn,
            _searchBox.Text,
            _filterComboBox.SelectedItem as string);
    }

    private Control CreateRow(EditableItem item)
    {
        var row = ManagerListShell.CreateRow("*,Auto,Auto,Auto,Auto");
        row.Margin = new Thickness(0, 1, ManagerListShell.ScrollBarClearance, 1);
        row.Children.Add(new TextBlock
        {
            Text = item.Name,
            Margin = new Thickness(10, 9),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var status = new TextBlock
        {
            Text = item.IsBuiltIn ? "Data.bar" : item.UsageCount > 0 ? $"Used by {item.UsageCount}" : "Custom",
            Foreground = Brushes.Gray,
            Margin = new Thickness(8, 9),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(status, 1);
        row.Children.Add(status);

        var editButton = CreateEditButton();
        ToolTip.SetTip(editButton, item.IsBuiltIn ? "View Culture" : "Edit Culture");
        editButton.Click += async (_, _) => await RunExclusiveAsync(
            () => _openEditorAsync(this, item.Name, item.IsBuiltIn));
        Grid.SetColumn(editButton, 2);
        row.Children.Add(editButton);

        var copyButton = CreateCopyButton();
        ToolTip.SetTip(copyButton, "Copy Culture");
        copyButton.Click += async (_, _) => await RunExclusiveAsync(() => DuplicateAsync(item));
        Grid.SetColumn(copyButton, 3);
        row.Children.Add(copyButton);

        if (!item.IsBuiltIn)
        {
            var deleteButton = new Button
            {
                Content = "×",
                FontSize = 16,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(4),
                Background = Brush.Parse("#992824"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (_, _) => await RunExclusiveAsync(() => DeleteAsync(item));
            Grid.SetColumn(deleteButton, 4);
            row.Children.Add(deleteButton);
            row.DoubleTapped += async (_, _) => await RunExclusiveAsync(() => RenameAsync(item));
        }

        return row;
    }

    private static Button CreateEditButton()
    {
        var button = CreateIconButton();
        button.Content = new TextBlock
        {
            Text = "✎",
            FontSize = 16,
            RenderTransform = new ScaleTransform(-1, 1),
            RenderTransformOrigin = RelativePoint.Center
        };
        return button;
    }

    private static Button CreateCopyButton()
    {
        var icon = new Canvas { Width = 16, Height = 16 };
        var back = new Border { Width = 10, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1) };
        Canvas.SetLeft(back, 5);
        Canvas.SetTop(back, 1);
        icon.Children.Add(back);
        var front = new Border { Width = 10, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1), Background = Brush.Parse("#191C1A") };
        Canvas.SetLeft(front, 1);
        Canvas.SetTop(front, 4);
        icon.Children.Add(front);
        var button = CreateIconButton();
        button.Content = icon;
        return button;
    }

    private static Button CreateIconButton() => new()
    {
        Width = 28,
        Height = 28,
        Padding = new Thickness(0),
        Margin = new Thickness(4),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private async Task RunExclusiveAsync(Func<Task> operation)
    {
        if (_operationInProgress) return;
        _operationInProgress = true;
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            await new Prompt(
                PromptType.Error,
                "Culture manager operation failed",
                exception.Message).ShowDialog(this);
        }
        finally
        {
            _operationInProgress = false;
        }
    }
}
