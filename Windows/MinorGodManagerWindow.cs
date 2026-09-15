using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record MinorGodManagerItem(string Name, string TechName, bool IsBuiltIn, int UsageCount);

internal sealed class MinorGodManagerWindow : SimpleWindow
{
    private sealed class EditableItem
    {
        public required string Name { get; set; }
        public required string TechName { get; set; }
        public required bool IsBuiltIn { get; init; }
        public required int UsageCount { get; set; }
    }

    private readonly List<EditableItem> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly ListBox _itemsList;
    private readonly TextBlock _footerText;
    private readonly Func<Window, string, bool, Task<MinorGodEditorResult?>> _openEditorAsync;
    private readonly Func<Window, string, Task<MinorGodManagerItem?>> _createAsync;
    private readonly Func<string, bool, string, Task<MinorGodManagerItem?>> _duplicateAsync;
    private readonly Func<string, string, Task<MinorGodManagerItem?>> _renameAsync;
    private readonly Func<string, Task<bool>> _deleteAsync;
    private readonly Func<string, int> _resolveUsageCount;
    private readonly Func<Window, string, Task<bool>> _validateDeleteAsync;
    private bool _operationInProgress;

    public MinorGodManagerWindow(
        IEnumerable<MinorGodManagerItem> items,
        Func<Window, string, bool, Task<MinorGodEditorResult?>> openEditorAsync,
        Func<Window, string, Task<MinorGodManagerItem?>> createAsync,
        Func<string, bool, string, Task<MinorGodManagerItem?>> duplicateAsync,
        Func<string, string, Task<MinorGodManagerItem?>> renameAsync,
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
            TechName = item.TechName,
            IsBuiltIn = item.IsBuiltIn,
            UsageCount = item.UsageCount
        }).ToList();

        Title = "Manage Minor Gods";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var shell = new ManagerListShell(
            "Search Minor Gods...",
            ["All", "Original", "Custom"],
            "");
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
        => MinorGodCatalog.IsValidName(name) &&
           !_items.Any(item => !ReferenceEquals(item, ignored) &&
               item.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task AddAsync()
    {
        var name = await PromptForNameAsync("New Minor God name:");
        if (name == null) return;
        var created = await _createAsync(this, name);
        if (created == null) return;
        var item = ToEditable(created);
        _items.Add(item);
        RefreshList();
        await OpenEditorAsync(item);
    }

    private async Task DuplicateAsync(EditableItem source)
    {
        var name = await PromptForNameAsync("Copy Minor God as:");
        if (name == null) return;
        var created = await _duplicateAsync(source.TechName, source.IsBuiltIn, name);
        if (created == null) return;
        var item = ToEditable(created);
        _items.Add(item);
        RefreshList();
        await OpenEditorAsync(item);
    }

    private async Task OpenEditorAsync(EditableItem item)
    {
        var result = await _openEditorAsync(this, item.TechName, item.IsBuiltIn);
        if (result != null) Apply(item, result.Name, result.TechName);
        else
        {
            item.UsageCount = _resolveUsageCount(item.TechName);
            RefreshList();
        }
    }

    private async Task RenameAsync(EditableItem item)
    {
        if (item.IsBuiltIn) return;
        var prompt = new InputPromptWindow(
            "Rename Minor God:",
            item.Name,
            confirmButtonText: "Save",
            allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null) return;
        var name = prompt.InputText.Trim();
        if (name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) return;
        if (!IsValidName(name, item))
        {
            await ShowInvalidNameAsync();
            return;
        }

        var renamed = await _renameAsync(item.TechName, name);
        if (renamed == null) return;
        Apply(item, renamed.Name, renamed.TechName);
    }

    private void Apply(EditableItem item, string name, string techName)
    {
        item.Name = name;
        item.TechName = techName;
        item.UsageCount = _resolveUsageCount(techName);
        RefreshList();
    }

    private async Task DeleteAsync(EditableItem item)
    {
        if (item.IsBuiltIn || !await _validateDeleteAsync(this, item.TechName)) return;
        var confirm = new Prompt(PromptType.Confirm, "Remove Minor God?",
            $"Are you sure you want to remove '{item.Name}'?", confirmButtonText: "Remove");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed || !await _deleteAsync(item.TechName)) return;
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
        => await new Prompt(PromptType.Error, "Invalid Minor God name",
            "Use a unique name containing only letters, digits, '_' or '-'.").ShowDialog(this);

    private void RefreshList()
    {
        _footerText.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count, _items.Count(item => item.IsBuiltIn), _items.Count(item => !item.IsBuiltIn),
            "");
        _itemsList.ItemsSource = ManagerListShell.FilterItems(
            _items, item => item.Name, item => item.IsBuiltIn,
            _searchBox.Text, _filterComboBox.SelectedItem as string);
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

        var edit = CreateEditButton();
        ToolTip.SetTip(edit, item.IsBuiltIn ? "View Minor God" : "Edit Minor God");
        edit.Click += async (_, _) => await RunExclusiveAsync(() => OpenEditorAsync(item));
        Grid.SetColumn(edit, 2);
        row.Children.Add(edit);

        var copy = CreateCopyButton();
        ToolTip.SetTip(copy, "Copy Minor God");
        copy.Click += async (_, _) => await RunExclusiveAsync(() => DuplicateAsync(item));
        Grid.SetColumn(copy, 3);
        row.Children.Add(copy);

        if (!item.IsBuiltIn)
        {
            var delete = CreateIconButton("×");
            delete.Background = Brush.Parse("#992824");
            delete.Foreground = Brushes.White;
            delete.Click += async (_, _) => await RunExclusiveAsync(() => DeleteAsync(item));
            Grid.SetColumn(delete, 4);
            row.Children.Add(delete);
            row.DoubleTapped += async (_, _) => await RunExclusiveAsync(() => RenameAsync(item));
        }
        return row;
    }

    private static EditableItem ToEditable(MinorGodManagerItem item) => new()
    {
        Name = item.Name,
        TechName = item.TechName,
        IsBuiltIn = item.IsBuiltIn,
        UsageCount = item.UsageCount
    };

    private static Button CreateIconButton(string text) => new()
    {
        Content = text,
        FontSize = 16,
        Width = 28,
        Height = 28,
        Padding = new Thickness(0),
        Margin = new Thickness(4),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Button CreateEditButton()
    {
        var button = CreateIconButton("");
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
        var back = new Border
        {
            Width = 10, Height = 12, BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1)
        };
        Canvas.SetLeft(back, 5);
        Canvas.SetTop(back, 1);
        icon.Children.Add(back);
        var front = new Border
        {
            Width = 10, Height = 12, BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(1),
            Background = Brush.Parse("#191C1A")
        };
        Canvas.SetLeft(front, 1);
        Canvas.SetTop(front, 4);
        icon.Children.Add(front);
        var button = CreateIconButton("");
        button.Content = icon;
        return button;
    }

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
                "Minor God manager operation failed",
                exception.Message).ShowDialog(this);
        }
        finally
        {
            _operationInProgress = false;
        }
    }
}
