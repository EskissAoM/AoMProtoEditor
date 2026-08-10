using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AoMDivineDataEditor.Classes;

namespace AoMDivineDataEditor.Windows;

public partial class InputPromptWindow : SimpleWindow
{
    public string? InputText { get; private set; }

    public InputPromptWindow()
    {
        InitializeComponent();
    }

    public InputPromptWindow(string message, string defaultValue = "", string confirmButtonText = "OK") : this()
    {
        _messageText.Text = message;
        _inputBox.Text = defaultValue;
        _confirmButton.Content = confirmButtonText;
        if (string.Equals(confirmButtonText, "Save", StringComparison.OrdinalIgnoreCase))
            _confirmButton.Background = Brush.Parse("#2b7a0b");
        Opened += (s, e) => _inputBox.Focus();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        InputText = _inputBox.Text?.Trim();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        InputText = null;
        Close();
    }
}
