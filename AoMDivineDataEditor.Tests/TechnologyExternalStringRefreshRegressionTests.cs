using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyExternalStringRefreshRegressionTests
{
    [Fact]
    public void Technology_save_offers_reload_and_merge_for_external_string_changes()
    {
        var root = FindProjectRoot();
        var technologyCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var parentCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("ReloadExternallyChangedStringsAsync()", technologyCode, StringComparison.Ordinal);
        Assert.Contains("confirmButtonText: \"Reload\"", technologyCode, StringComparison.Ordinal);
        Assert.Contains("prompt.Width = 560", technologyCode, StringComparison.Ordinal);
        Assert.Contains("prompt.Height = 220", technologyCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyStringMergePolicy.UseExternalVersionsForChangedIds", technologyCode, StringComparison.Ordinal);
        Assert.Contains("External strings reloaded and merged", technologyCode, StringComparison.Ordinal);
        Assert.Contains("_reloadStringsFromDisk?.Invoke()", technologyCode, StringComparison.Ordinal);
        Assert.Contains("InvalidateModStringEntriesCache,", parentCode, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the project root.");
    }
}
