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

    public InputPromptWindow(string message, string defaultValue = "", string confirmButtonText = "OK", bool allowWhitespace = true) : this()
    {
        _messageText.Text = message;
        _inputBox.Text = defaultValue;
        _confirmButton.Content = confirmButtonText;
        if (string.Equals(confirmButtonText, "Save", StringComparison.OrdinalIgnoreCase))
            _confirmButton.Background = Brush.Parse("#2b7a0b");
        if (!allowWhitespace)
        {
            var normalizing = false;
            _inputBox.TextChanged += (_, _) =>
            {
                if (normalizing)
                    return;
                var text = _inputBox.Text ?? "";
                var filtered = new string(text.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                if (string.Equals(text, filtered, StringComparison.Ordinal))
                    return;
                normalizing = true;
                _inputBox.Text = filtered;
                _inputBox.CaretIndex = filtered.Length;
                normalizing = false;
            };
        }
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
