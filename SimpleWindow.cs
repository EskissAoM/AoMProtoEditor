using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AoMDivineDataEditor.Classes;

/// <summary>Minimal window base shared by the extracted Proto Editor dialogs.</summary>
public abstract class SimpleWindow : Window, INotifyPropertyChanged
{
    private const double WindowFrameAllowance = 32;

    protected SimpleWindow()
    {
        string assemblyName = typeof(SimpleWindow).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The editor assembly has no name.");
        Icon = new WindowIcon(AssetLoader.Open(new Uri($"avares://{assemblyName}/Assets/editor_icon.png")));
        Opened += (_, _) => FitStartupSizeToWorkingArea();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void OnSelfChanged([CallerMemberName] string propertyName = "") => OnPropertyChanged(propertyName);

    /// <summary>
    /// Keeps the configured logical size when it fits on the current display,
    /// otherwise lets the platform maximize the window inside the usable area.
    /// Screen working areas are reported in physical pixels, so the comparison must
    /// account for the display scaling factor.
    /// </summary>
    private void FitStartupSizeToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null || screen.Scaling <= 0)
            return;

        var requestedWidth = double.IsFinite(Width) ? Width : Bounds.Width;
        var requestedHeight = double.IsFinite(Height) ? Height : Bounds.Height;
        var availableWidth = screen.WorkingArea.Width / screen.Scaling;
        var availableHeight = screen.WorkingArea.Height / screen.Scaling;
        if (requestedWidth + WindowFrameAllowance > availableWidth ||
            requestedHeight + WindowFrameAllowance > availableHeight)
        {
            WindowState = WindowState.Maximized;
        }
    }

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
