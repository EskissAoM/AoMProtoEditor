using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class PowerXmlTestHelpers
{
    internal static async Task UnlockAsync(PowerEditorView view)
    {
        view.FindControl<Button>("_editXmlButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var raw = view.FindControl<TextEditor>("_rawXmlEditor")!;
        for (var attempt = 0; raw.IsReadOnly && attempt < 200; attempt++) await Task.Delay(10);
        Assert.False(raw.IsReadOnly);
    }
}
