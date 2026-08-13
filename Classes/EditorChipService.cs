using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Classes;

public static class EditorChipService
{
    public static Border CreateBlueChip(
        string text,
        Action? onRemove,
        bool readOnly = false,
        Control? marker = null)
    {
        var border = new Border
        {
            Background = Brush.Parse("#3a5a78"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (marker != null)
        {
            marker.Margin = new Thickness(6, 0, 1, 0);
            marker.VerticalAlignment = VerticalAlignment.Center;
            stack.Children.Add(marker);
        }

        if (!readOnly && onRemove != null)
        {
            var button = new Button { Classes = { "chip-remove-button" } };
            button.Click += (_, _) => onRemove();
            stack.Children.Add(button);
        }

        border.Child = stack;
        return border;
    }
}
