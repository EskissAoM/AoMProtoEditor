using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass4ReferenceIntegrityTests
{
    [Fact]
    public void DeletionAndAutomaticRenameUseTheSharedTechnologyReferenceIndexWithoutBrowserCounts()
    {
        var view = ReadSource("Windows", "TechnologyEditorView.axaml.cs");
        var window = ReadSource("Windows", "ProtoEditorWindow.axaml.cs");
        var dialog = ReadSource("Windows", "DeletionBlockedDialog.cs");

        Assert.Contains("RefreshTechnologyReferenceUsage", view, StringComparison.Ordinal);
        Assert.Contains("DeletionBlockedDialog.ShowAsync", view, StringComparison.Ordinal);
        Assert.Contains("{entityLabel} is still in use", dialog, StringComparison.Ordinal);
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
        => File.ReadAllText(Path.Combine([ProjectSource.RootPath, .. relativePath]));

}
