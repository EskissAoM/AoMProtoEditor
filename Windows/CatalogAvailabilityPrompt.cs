using AoMDivineDataEditor.Classes;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

internal static class CatalogAvailabilityPrompt
{
    internal static Task<bool> EnsureAsync<T>(Window owner, Func<CatalogLoadResult<T>> reload, string label)
        => EnsureAsync(owner, () => Task.FromResult(reload()), label,
            "Check access to this folder or resolve the reported limit/link, then retry.");

    internal static async Task<bool> EnsureAsync<T>(Window owner, Func<Task<CatalogLoadResult<T>>> reload,
        string label, string recovery)
    {
        while (true)
        {
            var result = await reload();
            if (result.IsComplete) return true;
            var prompt = new Prompt(PromptType.Confirm, $"{label} list incomplete",
                result.Warning + "\n\n" + recovery + " " +
                "Cancel leaves your files and edits unchanged.", confirmButtonText: "Retry").FitToContent(600, 360);
            await prompt.ShowDialog(owner);
            if (!prompt.Confirmed) return false;
        }
    }
}
