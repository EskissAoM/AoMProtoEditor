using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Constructs repeated visual primitives for dynamic Power editors. XML mutation,
/// visibility, event ordering, and editor state remain owned by PowerEditorView.
/// </summary>
public static class PowerEditorDynamicControls
{
    public static (Border Container, StackPanel Body) CreateCard()
    {
        var body = new StackPanel { Spacing = 4 };
        return (CreateCardSurface(body), body);
    }

    public static Border CreateCardSurface(Control child)
    {
        var container = new Border { Child = child };
        container.Classes.Add("temp-unit-card");
        return container;
    }

    public static Button CreateOptionalButton(string label, object? tag = null)
        => new() { Content = label, Tag = tag, Classes = { "add-component" } };

    public static Button CreateRemoveButton(object? tag = null)
        => new() { Tag = tag, Classes = { "remove-button" } };

    public static StackPanel CreateLabeledSegment(string label, Control control)
    {
        var segment = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrWhiteSpace(label))
        {
            segment.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4)
            });
        }
        segment.Children.Add(control);
        return segment;
    }

    public static AutoCompleteBox CreateStrictSelector(
        IEnumerable<string> values,
        string current,
        bool isEnabled,
        Func<bool> isBusy,
        Func<bool> canCommit,
        Action<string> commit,
        string tag,
        ScrollViewer ownerScroll,
        double width = 200)
    {
        var options = values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Append(current).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var box = new AutoCompleteBox
        {
            Width = width,
            MaxWidth = width,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            IsEnabled = isEnabled,
            Margin = new Thickness(0, 0, 14, 4),
            Tag = tag
        };
        EditorAutoCompleteService.ConfigureStrict(box, options, current, isBusy,
            preserveUnknownInitialValue: true, allowEmpty: true, commitEmptyAsValid: true,
            selectAllOnFirstClick: false, valueCommitted: value =>
            {
                if (isBusy() || !canCommit()) return;
                commit(value);
            });
        box.Text = current;
        EditorAutoCompleteService.FreezeOwnerScrollWhileDropDownIsOpen(box, ownerScroll);
        return box;
    }

    public static Border CreateChip(
        string text,
        Action? onRemove,
        bool readOnly = false,
        Control? marker = null)
        => EditorChipService.CreateBlueChip(text, onRemove, readOnly, marker);
}
