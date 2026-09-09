using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass4ReferenceIntegrityTests
{
    [Fact]
    public void DeletionAndAutomaticRenameUseTheSharedTechnologyReferenceIndexWithoutBrowserCounts()
    {
        var view = ReadSource("Windows", "TechnologyEditorView.axaml.cs");
        var window = ReadSource("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("RefreshTechnologyReferenceUsage", view, StringComparison.Ordinal);
        Assert.Contains("Technology is still in use", view, StringComparison.Ordinal);
        Assert.Contains("TechnologyReferenceIndex.AddOrCollapseRename", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Rename technology references", view, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTechnologyBrowserDisplayName", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceMigrationParticipatesInTheExistingAtomicSaveTransaction()
    {
        var view = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("paths.AddRange(externalMigration.Paths)", view, StringComparison.Ordinal);
        Assert.Contains("expectedStates[state.Key] = state.Value", view, StringComparison.Ordinal);
        Assert.Contains("externalMigration?.Save", view, StringComparison.Ordinal);
        Assert.Contains("FileIntegrityTransaction.ExecuteAsync(paths, expectedStates", view, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([FindProjectRoot(), .. relativePath]));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
