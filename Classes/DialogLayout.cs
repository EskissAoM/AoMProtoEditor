using Avalonia;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Classes;

/// <summary>Logical client-size limits shared by compact, scrollable dialogs.</summary>
internal static class DialogLayout
{
    internal static Size Limits(Window dialog, double width, double height)
    {
        var screen = dialog.Screens.ScreenFromWindow(dialog.Owner ?? dialog) ?? dialog.Screens.Primary;
        if (screen is { Scaling: > 0 })
        {
            width = Math.Min(width, screen.WorkingArea.Width / screen.Scaling - 48);
            height = Math.Min(height, screen.WorkingArea.Height / screen.Scaling - 64);
        }
        if (dialog.Owner is Window { ClientSize.Width: > 0, ClientSize.Height: > 0 } owner)
        {
            width = Math.Min(width, owner.ClientSize.Width - 32);
            height = Math.Min(height, owner.ClientSize.Height - 32);
        }
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }
}
