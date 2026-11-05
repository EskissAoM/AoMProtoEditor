using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record ProtoUnitCommandManagerItem(string Name, bool IsBuiltIn, int UsageCount);

internal sealed class ProtoUnitCommandsManagerWindow : SimpleWindow
{
    private sealed class EditableProtoUnitCommandItem
    {
        public required string OriginalName { get; set; }
        public required string Name { get; set; }
        public required bool IsBuiltIn { get; init; }
        public required int UsageCount { get; set; }
    }

    private readonly List<EditableProtoUnitCommandItem> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly ListBox _itemsList;
    private readonly TextBlock _footerHint;
    private readonly Func<Window, string, bool, Task<string?>>? _openEditorAsync;
    private readonly Func<string, Task<bool>>? _createCommandAsync;
    private readonly Func<string, bool, string, Task<bool>>? _duplicateCommandAsync;
    private readonly Func<string, string, Task<bool>>? _renameCommandAsync;
    private readonly Func<string, Task<bool>>? _deleteCommandAsync;
    private readonly Func<string, int>? _resolveUsageCount;
    private readonly Func<Window, string, Task<bool>>? _validateDeleteAsync;

    public ProtoUnitCommandsManagerWindow(
        IEnumerable<ProtoUnitCommandManagerItem> items,
        Func<Window, string, bool, Task<string?>>? openEditorAsync = null,
        Func<string, Task<bool>>? createCommandAsync = null,
        Func<string, bool, string, Task<bool>>? duplicateCommandAsync = null,
        Func<string, string, Task<bool>>? renameCommandAsync = null,
        Func<string, Task<bool>>? deleteCommandAsync = null,
        Func<string, int>? resolveUsageCount = null,
        Func<Window, string, Task<bool>>? validateDeleteAsync = null)
    {
        _openEditorAsync = openEditorAsync;
        _createCommandAsync = createCommandAsync;
        _duplicateCommandAsync = duplicateCommandAsync;
        _renameCommandAsync = renameCommandAsync;
        _deleteCommandAsync = deleteCommandAsync;
        _resolveUsageCount = resolveUsageCount;
        _validateDeleteAsync = validateDeleteAsync;
        Title = "Manage ProtoUnit Commands";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        _items = items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new EditableProtoUnitCommandItem
            {
                OriginalName = item.Name,
                Name = item.Name,
                IsBuiltIn = item.IsBuiltIn,
                UsageCount = item.UsageCount
            })
            .ToList();

        var shell = new ManagerListShell(
            "Search commands...",
            ["All", "Original", "Custom"],
            "");
        _searchBox = shell.SearchBox;
        _filterComboBox = shell.FilterComboBox;
        _footerHint = shell.FooterTextBlock;
        _itemsList = ManagerListShell.CreateVirtualizedList<EditableProtoUnitCommandItem>(CreateRow);
        shell.ReplaceItemsHost(_itemsList);
        shell.BindFiltering(RefreshList);
        shell.AddButton.Click += async (_, _) => await AddCommandAsync();
        Content = shell;
        RefreshList();
    }

    private bool IsValidName(string name, EditableProtoUnitCommandItem? ignore = null)
    {
        var trimmed = name.Trim();
        return InternalNamePolicy.IsValid(trimmed) &&
               !_items.Any(item => !ReferenceEquals(item, ignore) && item.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AddCommandAsync()
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        var prompt = new InputPromptWindow("New command name:", confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid command name", "Use a unique name containing only letters, digits, '_' or '-'.");
            return;
        }
        if (_createCommandAsync != null && !await _createCommandAsync(name)) return;
        var createdItem = new EditableProtoUnitCommandItem { OriginalName = name, Name = name, IsBuiltIn = false, UsageCount = 0 };
        _items.Add(createdItem);
        RefreshList();
        if (_openEditorAsync != null)
        {
            var updatedName = await _openEditorAsync(this, createdItem.Name, false);
            if (!string.IsNullOrWhiteSpace(updatedName) && !updatedName.Equals(createdItem.Name, StringComparison.OrdinalIgnoreCase))
            {
                createdItem.Name = updatedName;
                createdItem.OriginalName = updatedName;
                RefreshList();
            }
        }
    }

    private async Task DuplicateCommandAsync(EditableProtoUnitCommandItem item)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        var prompt = new InputPromptWindow("Duplicate command as:", confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid command name", "Use a unique name containing only letters, digits, '_' or '-'.");
            return;
        }
        if (_duplicateCommandAsync == null || !await _duplicateCommandAsync(item.Name, item.IsBuiltIn, name)) return;
        var duplicatedItem = new EditableProtoUnitCommandItem { OriginalName = name, Name = name, IsBuiltIn = false, UsageCount = 0 };
        _items.Add(duplicatedItem);
        RefreshList();
        if (_openEditorAsync != null)
        {
            var updatedName = await _openEditorAsync(this, duplicatedItem.Name, false);
            if (!string.IsNullOrWhiteSpace(updatedName) && !updatedName.Equals(duplicatedItem.Name, StringComparison.OrdinalIgnoreCase))
            {
                duplicatedItem.Name = updatedName;
                duplicatedItem.OriginalName = updatedName;
                RefreshList();
            }
        }
    }

    private async Task RenameCommandAsync(EditableProtoUnitCommandItem item)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        if (item.IsBuiltIn) return;
        var prompt = new InputPromptWindow("Rename command:", item.Name, confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var newName = prompt.InputText.Trim();
        if (newName.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) return;
        if (!IsValidName(newName, item))
        {
            await ShowErrorAsync("Invalid command name", "Use a unique name containing only letters, digits, '_' or '-'.");
            return;
        }
        var oldName = item.Name;
        if (_renameCommandAsync != null && !await _renameCommandAsync(oldName, newName))
            return;
        item.Name = newName;
        item.OriginalName = newName;
        RefreshList();
    }

    private async Task DeleteCommandAsync(EditableProtoUnitCommandItem item)
    {
        using var activity = (Content as ManagerListShell)?.BeginActivity();
        if (item.IsBuiltIn) return;
        item.UsageCount = Math.Max(item.UsageCount, _resolveUsageCount?.Invoke(item.Name) ?? 0);
        if (_validateDeleteAsync != null)
        {
            if (!await _validateDeleteAsync(this, item.Name))
                return;
        }
        else if (item.UsageCount > 0)
        {
            await ShowErrorAsync("Command is in use", $"'{item.Name}' is used by {item.UsageCount} unit(s) and cannot be removed.");
            return;
        }

        var confirm = new Prompt(
            PromptType.Confirm,
            "Remove command?",
            $"Are you sure you want to remove '{item.Name}'?",
            confirmButtonText: "Delete command", actionRole: PromptActionRole.Destructive);
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed)
            return;

        if (_deleteCommandAsync == null || !await _deleteCommandAsync(item.Name))
            return;

        _items.Remove(item);
        RefreshList();
    }

    private void RefreshList()
    {
        _footerHint.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => item.IsBuiltIn),
            _items.Count(item => !item.IsBuiltIn),
            "Double-click a custom command name to rename it.");
        _itemsList.ItemsSource = ManagerListShell.FilterItems(
            _items,
            item => item.Name,
            item => item.IsBuiltIn,
            _searchBox.Text,
            _filterComboBox.SelectedItem as string);
    }

    private Control CreateRow(EditableProtoUnitCommandItem item)
    {
            var row = ManagerListShell.CreateActionRow(3);
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

            var editButton = ManagerActionButtons.Edit(item.IsBuiltIn ? "View command" : "Edit command");
            editButton.Click += async (_, _) =>
            {
                using var activity = (Content as ManagerListShell)?.BeginActivity();
                if (_openEditorAsync == null) return;
                var updatedName = await _openEditorAsync(this, item.Name, item.IsBuiltIn);
                if (!item.IsBuiltIn && !string.IsNullOrWhiteSpace(updatedName) &&
                    !updatedName.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                {
                    item.Name = updatedName;
                    item.OriginalName = updatedName;
                    RefreshList();
                }
            };
            Grid.SetColumn(editButton, 2);
            row.Children.Add(editButton);

            var duplicateButton = ManagerActionButtons.Copy("Duplicate command");
            duplicateButton.Click += async (_, _) => await DuplicateCommandAsync(item);
            Grid.SetColumn(duplicateButton, 3);
            row.Children.Add(duplicateButton);

            if (!item.IsBuiltIn)
            {
                var deleteButton = ManagerActionButtons.Delete("Delete command");
                deleteButton.Click += async (_, _) => await DeleteCommandAsync(item);
                Grid.SetColumn(deleteButton, 4);
                row.Children.Add(deleteButton);
                row.DoubleTapped += async (_, _) => await RenameCommandAsync(item);
            }
        return row;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var prompt = new Prompt(PromptType.Error, title, message);
        await prompt.ShowDialog(this);
    }


}
