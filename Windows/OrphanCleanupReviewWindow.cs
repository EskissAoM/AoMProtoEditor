using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

/// <summary>
/// Explicit review for strings which may have become orphaned. Nothing is
/// selected by default: keeping user-authored data is always the safe action.
/// </summary>
internal sealed class OrphanCleanupReviewWindow : SimpleWindow
{
    private readonly List<(string Id, CheckBox CheckBox)> _rows = [];
    private readonly Button _removeButton;

    public OrphanCleanupReviewWindow(IEnumerable<string> candidateIds, string operationLabel)
    {
        Title = "Review possible orphan strings";
        Width = 660;
        Height = 440;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var candidates = candidateIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new StackPanel { Spacing = 5 };
        foreach (var id in candidates)
        {
            var checkBox = new CheckBox { Content = id, IsChecked = false };
            checkBox.IsCheckedChanged += (_, _) => UpdateRemoveButton();
            _rows.Add((id, checkBox));
            rows.Children.Add(checkBox);
        }

        _removeButton = new Button
        {
            Content = "Remove selected (0)",
            IsEnabled = false,
            Classes = { "positive" }
        };
        _removeButton.Click += (_, _) =>
        {
            SelectedIds = _rows.Where(row => row.CheckBox.IsChecked == true)
                .Select(row => row.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Close();
        };

        var keepButton = new Button { Content = "Keep all" };
        keepButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { keepButton, _removeButton }
        };

        Content = new Border
        {
            Padding = new Thickness(18),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Possible orphan strings",
                        FontSize = 18,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        [Grid.RowProperty] = 1,
                        Text = $"{operationLabel} no longer needs the entries below. They are kept unless you explicitly select them. " +
                               "A final mod-wide reference check is performed before any selected entry is removed.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Border
                    {
                        [Grid.RowProperty] = 2,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(10),
                        Child = new ScrollViewer { Content = rows }
                    },
                    new ContentControl
                    {
                        [Grid.RowProperty] = 3,
                        Content = buttons
                    }
                }
            }
        };
    }

    public IReadOnlySet<string> SelectedIds { get; private set; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void UpdateRemoveButton()
    {
        var count = _rows.Count(row => row.CheckBox.IsChecked == true);
        _removeButton.IsEnabled = count > 0;
        _removeButton.Content = $"Remove selected ({count})";
    }
}
