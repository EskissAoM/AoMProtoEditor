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
    private readonly TextBlock _footerText;
    private readonly Func<string, Task<bool>> _createAsync;
    private readonly Func<string, string, Task<bool>> _renameAsync;
    private readonly Func<string, Task<bool>> _deleteAsync;
    private readonly Func<string, int> _resolveUsageCount;

    public UnitTypeManagerWindow(
        IEnumerable<UnitTypeManagerItem> items,
        Func<string, Task<bool>> createAsync,
        Func<string, string, Task<bool>> renameAsync,
        Func<string, Task<bool>> deleteAsync,
        Func<string, int> resolveUsageCount)
    {
        _createAsync = createAsync;
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
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

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
        _footerText = shell.FooterTextBlock;
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
        _footerText.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => item.IsBuiltIn),
            _items.Count(item => !item.IsBuiltIn),
            "Double-click a custom Unit Type name to rename it.");
        _itemsPanel.Children.Clear();
        var search = _searchBox.Text?.Trim() ?? "";
        var filter = _filterComboBox.SelectedItem as string ?? "All";
        foreach (var item in _items
                     .Where(item => filter == "All" || filter == "Original" && item.IsBuiltIn || filter == "Custom" && !item.IsBuiltIn)
                     .Where(item => search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!item.IsBuiltIn) item.UsageCount = _resolveUsageCount(item.Name);
            var row = ManagerListShell.CreateRow("*,Auto,Auto");
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
                deleteButton.Click += async (_, _) => await DeleteAsync(item);
                Grid.SetColumn(deleteButton, 2);
                row.Children.Add(deleteButton);
                row.DoubleTapped += async (_, _) => await RenameAsync(item);
            }
            _itemsPanel.Children.Add(row);
        }
    }
}
