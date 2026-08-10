using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CryBarEditor.Controls;

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
        var dialog = new Window
        {
            Title = "Remove Transform command",
            Width = 560,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#141414"),
            Foreground = Brush.Parse("#d9d9d9")
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        root.Children.Add(new TextBlock
        {
            Text = $"Remove '{commandName}' from this ProtoUnit only, or remove the custom command completely?",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 5) };
        cancel.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.Cancel; dialog.Close(); };
        var onlyThisUnit = new Button { Content = "Only this unit", Padding = new Thickness(12, 5) };
        onlyThisUnit.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.OnlyThisUnit; dialog.Close(); };
        var removeTotally = new Button
        {
            Content = "Remove totally",
            Background = Brush.Parse("#2b7a0b"),
            Padding = new Thickness(12, 5)
        };
        removeTotally.Click += (_, _) => { choice = ProtoUnitTransformRemovalChoice.RemoveTotally; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(onlyThisUnit);
        buttons.Children.Add(removeTotally);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        dialog.Content = root;

        await dialog.ShowDialog(owner);
        return choice;
    }
}
