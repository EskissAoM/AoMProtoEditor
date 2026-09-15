using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

internal sealed class MinorGodAgePromptWindow : SimpleWindow
{
    public string? SelectedAgePrefix { get; private set; }

    public MinorGodAgePromptWindow(string godName)
    {
        Title = "Create Minor God Technology";
        Width = 420;
        Height = 190;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#111311");
        Foreground = Brush.Parse("#E8DECC");

        var root = new Grid
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14
        };
        root.Children.Add(new TextBlock
        {
            Text = $"Create the AgeUpgrade technology for {godName}.",
            FontWeight = FontWeight.SemiBold
        });

        var ageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        ageRow.Children.Add(new TextBlock { Text = "Age", VerticalAlignment = VerticalAlignment.Center });
        var age = new ComboBox
        {
            Width = 160,
            ItemsSource = new[] { "Classical", "Heroic", "Mythic" },
            SelectedIndex = 0
        };
        ageRow.Children.Add(age);
        Grid.SetRow(ageRow, 1);
        root.Children.Add(ageRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", Classes = { "positive" }, MinWidth = 90 };
        save.Click += (_, _) =>
        {
            SelectedAgePrefix = (age.SelectedItem as string) + "Age";
            Close(SelectedAgePrefix);
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }
}
