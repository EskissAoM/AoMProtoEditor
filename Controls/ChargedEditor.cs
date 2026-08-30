using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared visual shell for a full charged container. The host retains charged
/// field semantics, optional-row behavior, validation, and XML serialization.
/// </summary>
public sealed class ChargedEditor : Border
{
    public Grid Root { get; }
    public StackPanel Body { get; }
    public Button? RemoveButton { get; }

    public ChargedEditor(bool isReadOnly, bool showRemoveButton)
    {
        Background = Brush.Parse("#181818");
        BorderBrush = Brush.Parse("#4C4031");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8);

        Root = new Grid();
        Body = new StackPanel { Spacing = 5 };
        Root.Children.Add(Body);
        Child = Root;

        if (!isReadOnly && showRemoveButton)
        {
            RemoveButton = new Button
            {
                Classes = { "remove-button" },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            Root.Children.Add(RemoveButton);
        }
    }
}
