using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CryBarEditor.Classes;

namespace CryBarEditor.Windows;

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
    private readonly StackPanel _itemsPanel;
    private readonly Func<Window, string, bool, Task<string?>>? _openEditorAsync;
    private readonly Func<string, Task<bool>>? _createCommandAsync;
    private readonly Func<string, bool, string, Task<bool>>? _duplicateCommandAsync;
    private readonly Func<string, string, Task<bool>>? _renameCommandAsync;
    private readonly Func<string, Task<bool>>? _deleteCommandAsync;
    private readonly Func<string, int>? _resolveUsageCount;

    public ProtoUnitCommandsManagerWindow(
        IEnumerable<ProtoUnitCommandManagerItem> items,
        Func<Window, string, bool, Task<string?>>? openEditorAsync = null,
        Func<string, Task<bool>>? createCommandAsync = null,
        Func<string, bool, string, Task<bool>>? duplicateCommandAsync = null,
        Func<string, string, Task<bool>>? renameCommandAsync = null,
        Func<string, Task<bool>>? deleteCommandAsync = null,
        Func<string, int>? resolveUsageCount = null)
    {
        _openEditorAsync = openEditorAsync;
        _createCommandAsync = createCommandAsync;
        _duplicateCommandAsync = duplicateCommandAsync;
        _renameCommandAsync = renameCommandAsync;
        _deleteCommandAsync = deleteCommandAsync;
        _resolveUsageCount = resolveUsageCount;
        Title = "Manage ProtoUnit Commands";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#141414");
        Foreground = Brush.Parse("#d9d9d9");

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
        _searchBox = new TextBox
        {
            PlaceholderText = "Search commands...",
            Margin = new Thickness(0, 0, 8, 0)
        };
        _searchBox.TextChanged += (_, _) => RefreshList();
        topPanel.Children.Add(_searchBox);

        _filterComboBox = new ComboBox
        {
            ItemsSource = new[] { "All", "Original", "Custom" },
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _filterComboBox.SelectionChanged += (_, _) => RefreshList();
        Grid.SetColumn(_filterComboBox, 1);
        topPanel.Children.Add(_filterComboBox);

        var addButton = new Button
        {
            Content = "+",
            FontSize = 22,
            Width = 40,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brush.Parse("#2b7a0b"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        addButton.Click += async (_, _) => await AddCommandAsync();
        Grid.SetColumn(addButton, 2);
        topPanel.Children.Add(addButton);
        root.Children.Add(topPanel);

        _itemsPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var scroll = new ScrollViewer
        {
            Content = _itemsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        footer.Children.Add(new TextBlock
        {
            Text = "Double-click a custom command name to rename it. Changes are saved immediately.",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshList();
    }

    private bool IsValidName(string name, EditableProtoUnitCommandItem? ignore = null)
    {
        var trimmed = name.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               !_items.Any(item => !ReferenceEquals(item, ignore) && item.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AddCommandAsync()
    {
        var prompt = new InputPromptWindow("New command name:", confirmButtonText: "Save");
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid command name", "The name is empty or already exists.");
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
        var prompt = new InputPromptWindow("Duplicate command as:", confirmButtonText: "Save");
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid command name", "The name is empty or already exists.");
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
        if (item.IsBuiltIn) return;
        var prompt = new InputPromptWindow("Rename command:", item.Name, confirmButtonText: "Save");
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var newName = prompt.InputText.Trim();
        if (newName.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) return;
        if (!IsValidName(newName, item))
        {
            await ShowErrorAsync("Invalid command name", "The name is empty or already exists.");
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
        if (item.IsBuiltIn) return;
        item.UsageCount = Math.Max(item.UsageCount, _resolveUsageCount?.Invoke(item.Name) ?? 0);
        if (item.UsageCount > 0)
        {
            await ShowErrorAsync("Command is in use", $"'{item.Name}' is used by {item.UsageCount} unit(s) and cannot be removed.");
            return;
        }

        var confirm = new Prompt(
            PromptType.Confirm,
            "Remove command?",
            $"Are you sure you want to remove '{item.Name}'?",
            confirmButtonText: "Save");
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
        _itemsPanel.Children.Clear();
        var filter = _searchBox.Text?.Trim() ?? "";
        var sourceFilter = _filterComboBox.SelectedItem as string ?? "All";
        foreach (var item in _items
                     .Where(item => sourceFilter == "All" || (sourceFilter == "Original" && item.IsBuiltIn) || (sourceFilter == "Custom" && !item.IsBuiltIn))
                     .Where(item => string.IsNullOrWhiteSpace(filter) || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!item.IsBuiltIn && _resolveUsageCount != null) item.UsageCount = Math.Max(item.UsageCount, _resolveUsageCount(item.Name));
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
                Background = Brush.Parse("#202020"),
                Margin = new Thickness(0, 1)
            };
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

            var editButton = new Button
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
            ToolTip.SetTip(editButton, item.IsBuiltIn ? "View command" : "Edit command");
            editButton.Click += async (_, _) =>
            {
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
            ToolTip.SetTip(duplicateButton, "Duplicate command");
            duplicateButton.Click += async (_, _) => await DuplicateCommandAsync(item);
            Grid.SetColumn(duplicateButton, 3);
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
                    Foreground = Brushes.White,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                deleteButton.Click += async (_, _) => await DeleteCommandAsync(item);
                Grid.SetColumn(deleteButton, 4);
                row.Children.Add(deleteButton);
                row.DoubleTapped += async (_, _) => await RenameCommandAsync(item);
            }
            _itemsPanel.Children.Add(row);
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var prompt = new Prompt(PromptType.Error, title, message);
        await prompt.ShowDialog(this);
    }


}
