using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

public static class DocumentSafetyDialog
{
    public static async Task ShowAsync(Window owner, string documentLabel, string message)
    {
        var prompt = new Prompt(
            PromptType.Error,
            $"{documentLabel} file cannot be edited",
            message).FitToContent(700, 400);
        await prompt.ShowDialog(owner);
    }
}
