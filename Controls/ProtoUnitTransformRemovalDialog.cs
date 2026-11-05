using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Controls;

public enum ProtoUnitTransformRemovalChoice
{
    Cancel,
    OnlyThisUnit,
    RemoveTotally
}

public static class ProtoUnitTransformRemovalDialog
{
    public static async Task<ProtoUnitTransformRemovalChoice> ShowAsync(Window owner, string commandName)
    {
        var choice = ProtoUnitTransformRemovalChoice.Cancel;
        var dialog = new RemovalWindow
        {
            Title = "Remove Transform command",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#111311"),
            Foreground = Brush.Parse("#E8DECC")
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 16
        };
        var message = new ScrollViewer { MaxHeight = 220,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new TextBlock
        {
            Text = $"Remove '{commandName}' from this ProtoUnit only, or remove the custom command completely?",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        }};
        root.Children.Add(message);

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };
        cancel.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.Cancel; dialog.Close(); };
        var onlyThisUnit = new Button { Content = "Remove from this Unit", Classes = { "danger" } };
        onlyThisUnit.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.OnlyThisUnit; dialog.Close(); };
        var removeTotally = new Button
        {
            Content = "Delete command",
            Classes = { "danger" }
        };
        removeTotally.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.RemoveTotally; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(onlyThisUnit);
        buttons.Children.Add(removeTotally);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.Opened += (_, _) =>
        {
            var limits = DialogLayout.Limits(dialog, 560, 360);
            dialog.Width = limits.Width;
            dialog.MaxHeight = limits.Height;
            message.MaxHeight = Math.Max(1, limits.Height - 120);
            cancel.Focus();
        };

        await dialog.ShowDialog(owner);
        return choice;
    }

    private sealed class RemovalWindow : SimpleWindow { }
}
