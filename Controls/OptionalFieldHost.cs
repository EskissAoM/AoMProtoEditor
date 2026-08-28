using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Presents one optional editor as either an add button or a standard
/// label/editor/remove row. Field-specific state and XML mutations remain with
/// the caller; this control owns only the shared presentation and transition.
/// </summary>
public sealed class OptionalFieldHost : Grid
{
    private readonly string _label;
    private readonly bool _isReadOnly;
    private readonly Func<Task<bool>> _onAdd;
    private readonly Func<Task<bool>> _onRemove;

    public Control Editor { get; }
    public Button AddButton { get; }
    public Button RemoveButton { get; }
    public bool IsExpanded { get; private set; }

    public OptionalFieldHost(
        string label,
        Control editor,
        string addButtonText,
        bool isExpanded,
        bool isReadOnly,
        Func<Task<bool>> onAdd,
        Func<Task<bool>> onRemove)
    {
        _label = label;
        _isReadOnly = isReadOnly;
        _onAdd = onAdd;
        _onRemove = onRemove;
        Editor = editor;

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;

        AddButton = new Button
        {
            Content = addButtonText,
            Background = Brush.Parse("#2b7a0b"),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = !isReadOnly
        };
        AddButton.Click += async (_, _) =>
        {
            if (await _onAdd())
                SetExpanded(true);
        };

        RemoveButton = new Button
        {
            Classes = { "remove-button" },
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        RemoveButton.Click += async (_, _) =>
        {
            if (await _onRemove())
                SetExpanded(false);
        };

        SetExpanded(isExpanded);
    }

    public void SetExpanded(bool isExpanded)
    {
        IsExpanded = isExpanded;
        Children.Clear();

        if (!isExpanded)
        {
            ColumnDefinitions = new ColumnDefinitions("Auto");
            // Read-only/original actions show only values that actually exist.
            // A disabled add button suggests an inherited value where there is none.
            if (!_isReadOnly)
                Children.Add(AddButton);
            return;
        }

        ColumnDefinitions = _isReadOnly
            ? new ColumnDefinitions("Auto, Auto")
            : new ColumnDefinitions("Auto, Auto, Auto");

        Children.Add(new TextBlock
        {
            Text = _label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 10, 4)
        });

        Grid.SetColumn(Editor, 1);
        Children.Add(Editor);

        if (!_isReadOnly)
        {
            Grid.SetColumn(RemoveButton, 2);
            Children.Add(RemoveButton);
        }
    }
}
