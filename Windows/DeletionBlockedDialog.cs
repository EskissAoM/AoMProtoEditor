using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

internal static class DeletionBlockedDialog
{
    public static async Task ShowAsync(
        Window owner,
        string entityLabel,
        string identifier,
        IEnumerable<string> referenceLocations)
    {
        var references = referenceLocations
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shown = references.Take(12).Select(location => $"• {location}").ToList();
        if (references.Count > shown.Count)
            shown.Add($"• …and {references.Count - shown.Count} more reference(s)");

        var prompt = new Prompt(
            PromptType.Error,
            $"{entityLabel} is still in use",
            $"'{identifier}' is used by {references.Count} reference(s) and cannot be deleted. " +
            "Remove or change those references first.\n\n" + string.Join(Environment.NewLine, shown))
        {
            Width = 680,
            Height = 360,
            MinWidth = 560,
            MinHeight = 260
        };
        await prompt.ShowDialog(owner);
    }
}
