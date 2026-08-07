using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CryBarEditor.Classes;

/// <summary>Minimal window base shared by the extracted Proto Editor dialogs.</summary>
public abstract class SimpleWindow : Window, INotifyPropertyChanged
{
    protected SimpleWindow()
    {
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://AoMProtoEditor/Assets/editor_icon.png")));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void OnSelfChanged([CallerMemberName] string propertyName = "") => OnPropertyChanged(propertyName);

    protected static void FilterClear_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Parent is not Grid grid)
            return;

        foreach (var child in grid.Children)
        {
            if (child is TextBox textBox)
            {
                textBox.Text = "";
                break;
            }
        }
    }

    protected static void FilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.Parent is not Grid grid)
            return;

        foreach (var child in grid.Children)
        {
            if (child is Button button && button.Classes.Contains("filterClear"))
            {
                button.IsVisible = !string.IsNullOrEmpty(textBox.Text);
                break;
            }
        }
    }
}
