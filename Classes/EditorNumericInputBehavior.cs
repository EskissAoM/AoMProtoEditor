using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AoMDivineDataEditor.Classes;

public static class EditorNumericInputBehavior
{
    public static void AttachRule(TextBox textBox, ProtoUnitNumericKind kind)
    {
        var integerOnly = ProtoUnitStatsNumericRules.IsIntegerKind(kind);
        var allowNegative = ProtoUnitStatsNumericRules.AllowsNegativeInput(kind);
        var lastAcceptedText = textBox.Text ?? "";
        var restoring = false;

        bool IsAllowedIntermediate(string text)
        {
            if (text.Length == 0)
                return true;
            if (allowNegative && text == "-")
                return true;
            if (!integerOnly && (text == "." || allowNegative && text == "-."))
                return true;

            var start = allowNegative && text[0] == '-' ? 1 : 0;
            if (start == 1 && text.Length == 1)
                return true;
            var dotCount = 0;
            for (var index = start; index < text.Length; index++)
            {
                var ch = text[index];
                if (char.IsDigit(ch))
                    continue;
                if (!integerOnly && ch == '.' && ++dotCount == 1)
                    continue;
                return false;
            }
            return true;
        }

        textBox.AddHandler(InputElement.TextInputEvent, (_, args) =>
        {
            if (args.Text?.Any(ch => !char.IsDigit(ch) && (!allowNegative || ch != '-') && (integerOnly || ch != '.')) == true ||
                integerOnly && args.Text?.Contains('.', StringComparison.Ordinal) == true)
            {
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        textBox.TextChanged += (_, _) =>
        {
            if (restoring)
                return;
            var current = textBox.Text ?? "";
            if (IsAllowedIntermediate(current))
            {
                lastAcceptedText = current;
                return;
            }

            restoring = true;
            textBox.Text = lastAcceptedText;
            restoring = false;
        };
    }

    public static void AttachUnsignedDecimal(TextBox textBox)
    {
        EditorNumericFieldStyle.ConfigureNumericTextBox(textBox);
        textBox.AddHandler(InputElement.TextInputEvent, (_, args) =>
        {
            var proposed = (textBox.Text ?? "") + args.Text;
            if (!double.TryParse(proposed, out double _) && !string.Equals(proposed, ".", StringComparison.Ordinal))
                args.Handled = true;
        }, RoutingStrategies.Tunnel);
        textBox.TextChanged += (_, _) =>
        {
            var text = textBox.Text ?? "";
            var filtered = new string(text.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
            var dotIndex = filtered.IndexOf('.');
            if (dotIndex >= 0)
                filtered = filtered[..(dotIndex + 1)] + filtered[(dotIndex + 1)..].Replace(".", "", StringComparison.Ordinal);
            if (!string.Equals(text, filtered, StringComparison.Ordinal))
                textBox.Text = filtered;
        };
    }

    public static void AttachSignedDecimal(TextBox textBox)
    {
        EditorNumericFieldStyle.ConfigureNumericTextBox(textBox);
        textBox.AddHandler(InputElement.TextInputEvent, (_, args) =>
        {
            if (args.Text?.Any(ch => !char.IsDigit(ch) && ch != '.' && ch != '-') == true)
                args.Handled = true;
        }, RoutingStrategies.Tunnel);
        textBox.TextChanged += (_, _) =>
        {
            var text = textBox.Text ?? "";
            var filtered = new string(text.Where(ch => char.IsDigit(ch) || ch == '.' || ch == '-').ToArray());
            if (filtered.Contains('-'))
                filtered = "-" + filtered.Replace("-", "", StringComparison.Ordinal);
            var dotIndex = filtered.IndexOf('.');
            if (dotIndex >= 0)
                filtered = filtered[..(dotIndex + 1)] + filtered[(dotIndex + 1)..].Replace(".", "", StringComparison.Ordinal);
            if (!string.Equals(text, filtered, StringComparison.Ordinal))
                textBox.Text = filtered;
        };
    }
}
