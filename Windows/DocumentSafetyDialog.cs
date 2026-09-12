using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

public static class DocumentSafetyDialog
{
    public static async Task ShowAsync(Window owner, string documentLabel, string message)
    {
        var prompt = new Prompt(
            PromptType.Error,
            $"{documentLabel} file cannot be edited",
            message)
        {
            Width = 700,
            Height = 300,
            MinWidth = 580,
            MinHeight = 240
        };
        await prompt.ShowDialog(owner);
    }
}
