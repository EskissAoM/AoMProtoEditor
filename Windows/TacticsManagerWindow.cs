using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

internal sealed record TacticsManagerItem(string Name, bool IsBuiltIn, bool IsModifiedBuiltIn, int UsageCount);
internal sealed record TacticsRenameOperation(string OldName, string NewName);

internal sealed class TacticsManagerResult
{
    public List<string> CreatedNames { get; } = [];
    public List<TacticsRenameOperation> Renames { get; } = [];
    public List<string> DeletedNames { get; } = [];
}

internal sealed class TacticsManagerWindow : SimpleWindow
{
    private sealed class EditableTacticsItem
    {
        public string? OriginalName { get; init; }
        public string Name { get; set; } = "";
        public bool IsBuiltIn { get; init; }
        public bool IsModifiedBuiltIn { get; init; }
        public int UsageCount { get; init; }
    }

    private readonly List<EditableTacticsItem> _items;
    private readonly TextBox _searchBox;
    private readonly ComboBox _filterComboBox;
    private readonly StackPanel _itemsPanel;
    private readonly TextBlock _footerHint;
    private readonly Func<Window, string, bool, Task>? _openEditorAsync;
    private readonly Func<string, Task<bool>>? _createTacticsAsync;
    private readonly Func<string, bool, string, Task<bool>>? _duplicateTacticsAsync;
    private readonly Func<string, string, Task<bool>>? _renameTacticsAsync;
    private readonly Func<string, Task<bool>>? _deleteTacticsAsync;

    public TacticsManagerWindow(
        IEnumerable<TacticsManagerItem> items,
        Func<Window, string, bool, Task>? openEditorAsync = null,
        Func<string, Task<bool>>? createTacticsAsync = null,
        Func<string, bool, string, Task<bool>>? duplicateTacticsAsync = null,
        Func<string, string, Task<bool>>? renameTacticsAsync = null,
        Func<string, Task<bool>>? deleteTacticsAsync = null)
    {
        _openEditorAsync = openEditorAsync;
        _createTacticsAsync = createTacticsAsync;
        _duplicateTacticsAsync = duplicateTacticsAsync;
        _renameTacticsAsync = renameTacticsAsync;
        _deleteTacticsAsync = deleteTacticsAsync;
        Title = "Manage Tactics";
        Width = 700;
        Height = 650;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _items = items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new EditableTacticsItem
            {
                OriginalName = item.Name,
                Name = item.Name,
                IsBuiltIn = item.IsBuiltIn,
                IsModifiedBuiltIn = item.IsModifiedBuiltIn,
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
            PlaceholderText = "Search tactics...",
            Height = ManagerListShell.HeaderControlHeight,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _searchBox.TextChanged += (_, _) => RefreshList();
        topPanel.Children.Add(_searchBox);

        _filterComboBox = new ComboBox
        {
            ItemsSource = new[] { "All", "Original", "Custom" },
            SelectedIndex = 0,
            Height = ManagerListShell.HeaderControlHeight,
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
            Height = ManagerListShell.HeaderControlHeight,
            Padding = new Thickness(0),
            Classes = { "add-item" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        addButton.Click += async (_, _) => await AddTacticsAsync();
        Grid.SetColumn(addButton, 2);
        topPanel.Children.Add(addButton);
        root.Children.Add(topPanel);

        // Keep row actions comfortably clear of the ScrollViewer's vertical
        // scrollbar hit area. This shifts the whole row content slightly left.
        _itemsPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, ManagerListShell.ScrollBarClearance, 0)
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
        _footerHint = new TextBlock
        {
            Text = "",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_footerHint);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshList();
    }

    private static string NormalizeName(string value)
    {
        var name = value.Trim();
        if (!name.EndsWith(".tactics", StringComparison.OrdinalIgnoreCase))
            name += ".tactics";
        return name;
    }

    private bool IsValidName(string name)
    {
        if (!InternalNamePolicy.IsValidFileName(name, ".tactics"))
            return false;

        return !_items.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task AddTacticsAsync()
    {
        var prompt = new InputPromptWindow("New tactics name:", confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null)
            return;

        var name = NormalizeName(prompt.InputText);
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid tactics name", "The name is invalid or already exists.");
            return;
        }

        if (_createTacticsAsync != null && !await _createTacticsAsync(name))
            return;

        _items.Add(new EditableTacticsItem
        {
            OriginalName = name,
            Name = name,
            IsBuiltIn = false,
            IsModifiedBuiltIn = false,
            UsageCount = 0
        });
        RefreshList();
        if (_openEditorAsync != null)
            await _openEditorAsync(this, name, false);
    }

    private async Task DuplicateTacticsAsync(EditableTacticsItem item)
    {
        var prompt = new InputPromptWindow("Duplicate tactics as:", confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null)
            return;

        var name = NormalizeName(prompt.InputText);
        if (!IsValidName(name))
        {
            await ShowErrorAsync("Invalid tactics name", "The name is invalid or already exists.");
            return;
        }

        if (_duplicateTacticsAsync == null || !await _duplicateTacticsAsync(item.Name, item.IsBuiltIn, name))
            return;

        _items.Add(new EditableTacticsItem
        {
            OriginalName = name,
            Name = name,
            IsBuiltIn = false,
            IsModifiedBuiltIn = false,
            UsageCount = 0
        });
        RefreshList();
        if (_openEditorAsync != null)
            await _openEditorAsync(this, name, false);
    }

    private async Task RenameTacticsAsync(EditableTacticsItem item)
    {
        if (item.IsBuiltIn)
            return;

        var prompt = new InputPromptWindow("Rename tactics:", item.Name, confirmButtonText: "Save", allowWhitespace: false);
        await prompt.ShowDialog(this);
        if (prompt.InputText == null)
            return;

        var newName = NormalizeName(prompt.InputText);
        if (newName.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
            return;
        if (!IsValidName(newName))
        {
            await ShowErrorAsync("Invalid tactics name", "The name is invalid or already exists.");
            return;
        }

        if (_renameTacticsAsync == null || !await _renameTacticsAsync(item.Name, newName))
            return;

        item.Name = newName;
        RefreshList();
    }

    private async Task DeleteTacticsAsync(EditableTacticsItem item)
    {
        if (item.IsBuiltIn)
            return;
        if (item.UsageCount > 0)
        {
            await ShowErrorAsync(
                "Tactics is in use",
                $"'{item.Name}' is used by {item.UsageCount} unit(s) and cannot be removed.");
            return;
        }

        var confirm = new Prompt(
            PromptType.Confirm,
            "Remove tactics?",
            $"Are you sure you want to remove '{item.Name}'?",
            confirmButtonText: "Save");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed)
            return;

        if (_deleteTacticsAsync == null || !await _deleteTacticsAsync(item.Name))
            return;

        _items.Remove(item);
        RefreshList();
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var prompt = new Prompt(PromptType.Error, title, message);
        await prompt.ShowDialog(this);
    }

    private void RefreshList()
    {
        _footerHint.Text = ManagerListShell.FormatEntityCountFooter(
            _items.Count,
            _items.Count(item => item.IsBuiltIn),
            _items.Count(item => !item.IsBuiltIn),
            "Double-click a custom tactics name to rename it.");
        _itemsPanel.Children.Clear();
        var filter = _searchBox.Text?.Trim() ?? "";
        var sourceFilter = _filterComboBox.SelectedItem as string ?? "All";

        foreach (var item in _items
                     .Where(item => sourceFilter == "All" ||
                                    (sourceFilter == "Original" && item.IsBuiltIn) ||
                                    (sourceFilter == "Custom" && !item.IsBuiltIn))
                     .Where(item => string.IsNullOrWhiteSpace(filter) ||
                                    item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
                Background = Brush.Parse("#191C1A"),
                Margin = new Thickness(0, 1)
            };

            var nameBlock = new TextBlock
            {
                Text = item.Name,
                Margin = new Thickness(10, 9),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            row.Children.Add(nameBlock);

            var status = new TextBlock
            {
                Text = item.IsModifiedBuiltIn
                    ? "Data.bar (Modified)"
                    : item.IsBuiltIn
                        ? "Data.bar"
                        : item.UsageCount > 0
                            ? $"Used by {item.UsageCount}"
                            : "Custom",
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
            ToolTip.SetTip(editButton, item.IsBuiltIn ? "View tactics" : "Edit tactics");
            editButton.Click += async (_, _) => await OpenTacticsEditorAsync(item);
            Grid.SetColumn(editButton, 2);
            row.Children.Add(editButton);

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
                Background = Brush.Parse("#191C1A")
            };
            Canvas.SetLeft(frontPage, 1);
            Canvas.SetTop(frontPage, 4);
            duplicateIcon.Children.Add(frontPage);

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
            ToolTip.SetTip(duplicateButton, "Duplicate tactics");
            duplicateButton.Click += async (_, _) => await DuplicateTacticsAsync(item);
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
                    Background = Brush.Parse("#992824"),
                    Foreground = Brushes.White,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                deleteButton.Click += async (_, _) => await DeleteTacticsAsync(item);
                Grid.SetColumn(deleteButton, 4);
                row.Children.Add(deleteButton);
                row.DoubleTapped += async (_, _) => await RenameTacticsAsync(item);
            }

            _itemsPanel.Children.Add(row);
        }
    }

    private async Task OpenTacticsEditorAsync(EditableTacticsItem item)
    {
        if (_openEditorAsync != null)
        {
            await _openEditorAsync(this, item.Name, item.IsBuiltIn);
            return;
        }

        var window = new TacticsEditorWindow(item.Name, item.IsBuiltIn);
        await window.ShowDialog(this);
    }


}

internal sealed class TacticsEditorWindow : ProtoEditorWindow
{
    public TacticsEditorWindow()
        : base(initializeProtoEditor: false)
    {
    }

    public TacticsEditorWindow(string tacticsName, bool isBuiltIn)
        : base()
    {
        Title = isBuiltIn ? $"View Tactics - {tacticsName}" : $"Edit Tactics - {tacticsName}";
        Width = 760;
        Height = 620;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
