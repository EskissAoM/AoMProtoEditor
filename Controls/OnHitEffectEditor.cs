using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AoMDivineDataEditor.Controls;

/// <summary>
/// Shared shell for an on-hit-effect card. Effect-specific controls, catalogs,
/// validation, inheritance, and XML serialization remain owned by the host.
/// </summary>
public sealed class OnHitEffectEditor : Border
{
    public AutoCompleteBox TypeField { get; }
    public CheckBox ActiveField { get; }
    public StackPanel Body { get; }
    public Grid Header { get; }
    public Button? RemoveButton { get; }

    public OnHitEffectEditor(
        AutoCompleteBox typeField,
        CheckBox activeField,
        bool isReadOnly,
        bool isSupported,
        Control? sourceMarker = null)
    {
        TypeField = typeField;
        ActiveField = activeField;

        BorderBrush = Brush.Parse("#3f3f46");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8);
        Background = Brush.Parse("#202020");

        Body = new StackPanel { Spacing = 4 };
        Child = Body;

        Header = new Grid
        {
            ColumnDefinitions = !isReadOnly
                ? new ColumnDefinitions("Auto, 170, Auto, Auto, *, Auto, Auto")
                : new ColumnDefinitions("Auto, 170, Auto, Auto, *, Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Header.Children.Add(new TextBlock
        {
            Text = "Type:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        });

        Grid.SetColumn(TypeField, 1);
        Header.Children.Add(TypeField);

        var activeLabel = new TextBlock
        {
            Text = "Active:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 4, 8, 4)
        };
        Grid.SetColumn(activeLabel, 2);
        Header.Children.Add(activeLabel);

        Grid.SetColumn(ActiveField, 3);
        Header.Children.Add(ActiveField);

        if (!isSupported)
        {
            var unsupportedLabel = new TextBlock
            {
                Text = "Unsupported in editor for now; XML will be preserved.",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(12, 4, 0, 4)
            };
            Grid.SetColumn(unsupportedLabel, 4);
            Header.Children.Add(unsupportedLabel);
        }

        if (sourceMarker != null)
        {
            sourceMarker.Margin = new Thickness(6, 2, 6, 0);
            Grid.SetColumn(sourceMarker, 5);
            Header.Children.Add(sourceMarker);
        }

        if (!isReadOnly)
        {
            RemoveButton = new Button
            {
                Classes = { "remove-button" }
            };
            Grid.SetColumn(RemoveButton, 6);
            Header.Children.Add(RemoveButton);
        }

        Body.Children.Add(Header);
    }
}
