using AoMDivineDataEditor.Windows;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Xunit;

namespace AoMDivineDataEditor.Tests;

internal static class MajorGodXmlTestHelpers
{
    internal static async Task UnlockAsync(MajorGodEditorView view)
    {
        view.FindControl<Button>("_editXmlButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var raw = view.FindControl<TextEditor>("_rawXmlEditor")!;
        for (var i = 0; raw.IsReadOnly && i < 200; i++) await Task.Delay(10);
        Assert.False(raw.IsReadOnly);
    }
}
