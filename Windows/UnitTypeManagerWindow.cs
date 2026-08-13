using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record UnitTypeManagerItem(string Name, bool IsBuiltIn, int UsageCount);

internal sealed class UnitTypeManagerWindow : SimpleWindow
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
    private readonly StackPanel _itemsPanel;
    private readonly Func<string, Task<bool>> _createAsync;
    private readonly Func<string, bool, string, Task<bool>> _duplicateAsync;
    private readonly Func<string, string, Task<bool>> _renameAsync;
    private readonly Func<string, Task<bool>> _deleteAsync;
    private readonly Func<string, int> _resolveUsageCount;

    public UnitTypeManagerWindow(
        IEnumerable<UnitTypeManagerItem> items,
        Func<string, Task<bool>> createAsync,
        Func<string, bool, string, Task<bool>> duplicateAsync,
        Func<string, string, Task<bool>> renameAsync,
        Func<string, Task<bool>> deleteAsync,
        Func<string, int> resolveUsageCount)
    {
        _createAsync = createAsync;
        _duplicateAsync = duplicateAsync;
        _renameAsync = renameAsync;
        _deleteAsync = deleteAsync;
        _resolveUsageCount = resolveUsageCount;
        _items = items.Select(item => new EditableItem
        {
            Name = item.Name,
            IsBuiltIn = item.IsBuiltIn,
            UsageCount = item.UsageCount
        }).ToList();

        Title = "Manage Unit Types";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414");
        Foreground = Brush.Parse("#d9d9d9");

        var shell = new ManagerListShell(
            "Search Unit Types...",
            ["All", "Original", "Custom"],
            "Double-click a custom Unit Type name to rename it.");
        _searchBox = shell.SearchBox;
        _searchBox.TextChanged += (_, _) => RefreshList();
        _filterComboBox = shell.FilterComboBox;
        _filterComboBox.SelectionChanged += (_, _) => RefreshList();
        shell.AddButton.Click += async (_, _) => await AddAsync();
        _itemsPanel = shell.ItemsPanel;
        Content = shell;
        RefreshList();
    }

    private bool IsValidName(string name, EditableItem? ignored = null)
        => UnitTypeCatalog.IsValidName(name) &&
           !_items.Any(item => !ReferenceEquals(item, ignored) && item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task AddAsync()
    {
        var name = await PromptForNameAsync("New Unit Type name:");
        if (name == null || !await _createAsync(name)) return;
        _items.Add(new EditableItem { Name = name, IsBuiltIn = false, UsageCount = 0 });
        RefreshList();
    }

    private async Task DuplicateAsync(EditableItem item)
    {
        var name = await PromptForNameAsync("Duplicate Unit Type as:");
        if (name == null || !await _duplicateAsync(item.Name, item.IsBuiltIn, name)) return;
        _items.Add(new EditableItem { Name = name, IsBuiltIn = false, UsageCount = 0 });
        RefreshList();
    }

    private async Task RenameAsync(EditableItem item)
    {
        if (item.IsBuiltIn) return;
        var prompt = new InputPromptWindow("Rename Unit Type:", item.Name, confirmButtonText: "Save", allowWhitespace: false);
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
        var message = item.UsageCount > 0
            ? $"'{item.Name}' is used by {item.UsageCount} unit(s). Removing it will also remove it from those units. Continue?"
            : $"Are you sure you want to remove '{item.Name}'?";
        var confirm = new Prompt(PromptType.Confirm, "Remove Unit Type?", message, confirmButtonText: "Remove");
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
        => await new Prompt(PromptType.Error, "Invalid Unit Type name",
            "Use a unique name containing only letters, digits, '_' or '-'.").ShowDialog(this);

    private void RefreshList()
    {
        _itemsPanel.Children.Clear();
        var search = _searchBox.Text?.Trim() ?? "";
        var filter = _filterComboBox.SelectedItem as string ?? "All";
        foreach (var item in _items
                     .Where(item => filter == "All" || filter == "Original" && item.IsBuiltIn || filter == "Custom" && !item.IsBuiltIn)
                     .Where(item => search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!item.IsBuiltIn) item.UsageCount = _resolveUsageCount(item.Name);
            var row = ManagerListShell.CreateRow("*,Auto,Auto,Auto");
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

            var duplicateIcon = new Canvas { Width = 16, Height = 16 };
            var backPage = new Border { Width = 10, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1) };
            Canvas.SetLeft(backPage, 5); Canvas.SetTop(backPage, 1); duplicateIcon.Children.Add(backPage);
            var frontPage = new Border { Width = 10, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1), Background = Brush.Parse("#202020") };
            Canvas.SetLeft(frontPage, 1); Canvas.SetTop(frontPage, 4); duplicateIcon.Children.Add(frontPage);
            var duplicateButton = new Button
            {
                Content = duplicateIcon,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Margin = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(duplicateButton, "Duplicate Unit Type");
            duplicateButton.Click += async (_, _) => await DuplicateAsync(item);
            Grid.SetColumn(duplicateButton, 2);
            row.Children.Add(duplicateButton);

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
                    Background = Brush.Parse("#b00000"),
                    Foreground = Brushes.White
                };
                deleteButton.Click += async (_, _) => await DeleteAsync(item);
                Grid.SetColumn(deleteButton, 3);
                row.Children.Add(deleteButton);
                row.DoubleTapped += async (_, _) => await RenameAsync(item);
            }
            _itemsPanel.Children.Add(row);
        }
    }
}
