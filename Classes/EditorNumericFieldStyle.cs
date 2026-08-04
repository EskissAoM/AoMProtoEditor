using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CryBarEditor.Classes;

/// <summary>
/// Shared compact presentation for numeric editor controls. The constant is
/// sized for eight typical numeric glyphs plus the standard TextBox chrome.
/// Text is never truncated; TextBox keeps its normal horizontal scrolling.
/// </summary>
public static class EditorNumericFieldStyle
{
    public const double CompactWidth = 90;

    public static TextBox ConfigureNumericTextBox(TextBox textBox)
    {
        textBox.Width = CompactWidth;
        textBox.MaxWidth = CompactWidth;
        textBox.HorizontalAlignment = HorizontalAlignment.Left;
        textBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        textBox.Text = FormatNumericDisplay(textBox.Text);
        textBox.LostFocus += (_, _) => textBox.Text = FormatNumericDisplay(textBox.Text);
        return textBox;
    }

    public static string FormatDisplay(string? value) => FormatNumericDisplay(value);

    public static string FormatDisplay(string? value, int minimumFractionDigits)
    {
        var text = FormatNumericDisplay(value);
        if (minimumFractionDigits <= 0 || string.IsNullOrWhiteSpace(text))
            return text;

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return text;

        var dot = text.IndexOf('.');
        if (dot < 0)
            return text + "." + new string('0', minimumFractionDigits);

        var fractionLength = text.Length - dot - 1;
        return fractionLength >= minimumFractionDigits
            ? text
            : text + new string('0', minimumFractionDigits - fractionLength);
    }

    public static string FormatNumericDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;

        var text = value.Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return text;

        var dot = text.IndexOf('.');
        if (dot < 0)
            return text;

        text = text.TrimEnd('0');
        if (text.EndsWith(".", StringComparison.Ordinal))
            text = text[..^1];

        return text is "-0" or "+0" ? "0" : text;
    }
}
