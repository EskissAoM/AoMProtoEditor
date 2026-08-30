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
            Background = Brush.Parse("#193A52"),
            BorderBrush = Brush.Parse("#3D7898"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 3),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Brush.Parse("#D9EEF7"),
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
