using Xunit;

namespace AoMDivineDataEditor.Tests.Powers;

public sealed class PowerEditorHostSynchronizationTests
{
    [Fact]
    public void RenameDeleteAndSaveSynchronizeHostTabsDraftsAndCaches()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var browserChanged = ProjectSource.ExtractBetween(
            source,
            "private void PowerView_BrowserStateChanged",
            "private void PowerView_DirtyStateChanged");
        var saved = ProjectSource.ExtractBetween(
            source,
            "private void PowerView_Saved",
            "private IReadOnlyList<string> GetMajorGodTechnologyNames");
        var delete = ProjectSource.ExtractBetween(
            source,
            "if (_activeEntityKind == EditorEntityKind.Powers)",
            "await DeleteSelectedTechnologyAsync");
        var mainSave = ProjectSource.ExtractBetween(
            source,
            "private async void Save_Click",
            "private async Task SaveCurrentUnitCoreAsync");

        Assert.Contains("RenameOpenDocumentTabs(EditorEntityKind.Powers", browserChanged,
            StringComparison.Ordinal);
        Assert.Contains("RenamePendingAbilityDraftReferences(previousName!, currentName)",
            browserChanged, StringComparison.Ordinal);
        Assert.Contains("_abilityPowerCatalog.Clear()", saved, StringComparison.Ordinal);
        Assert.Contains("_abilityDrafts.Clear()", saved, StringComparison.Ordinal);
        Assert.Contains("BuildEditorPanel(_currentUnitName, resetScroll: false)", saved,
            StringComparison.Ordinal);
        Assert.Contains("!_powerView.GetPowerNames(modified: true)", delete,
            StringComparison.Ordinal);
        Assert.Contains("RemoveDeletedDocumentTabs(EditorEntityKind.Powers, deletedName)", delete,
            StringComparison.Ordinal);
        Assert.Contains("RefreshSavedDocumentSnapshots(EditorEntityKind.Powers)", mainSave,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PowerSaveDoesNotDiscardUnsavedUnitDrafts()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var saved = ProjectSource.ExtractBetween(
            source,
            "private void PowerView_Saved",
            "private IReadOnlyList<string> GetMajorGodTechnologyNames");

        var guard = saved.IndexOf("if (!_isDirty)", StringComparison.Ordinal);
        var clear = saved.IndexOf("_unitAbilityDrafts.Clear()", StringComparison.Ordinal);
        var rebuild = saved.IndexOf("BuildEditorPanel(_currentUnitName, resetScroll: false)",
            StringComparison.Ordinal);
        Assert.True(guard >= 0 && clear > guard && rebuild > clear);
    }

    [Fact]
    public void NewPowerKeepsItsInternalSourceSwitchOutOfUserNavigationValidation()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var add = ProjectSource.ExtractBetween(
            source,
            "private async Task AddPowerFromBrowserAsync",
            "private async Task AddTechnologyFromBrowserAsync");

        var suppress = add.IndexOf("_documentTabActivationInProgress = true", StringComparison.Ordinal);
        var create = add.IndexOf("await _powerView.AddPowerAsync(duplicateSelected)", StringComparison.Ordinal);
        var release = add.IndexOf("_documentTabActivationInProgress = false", StringComparison.Ordinal);
        Assert.True(suppress >= 0 && create > suppress && release > create,
            "The complete Power creation transaction must suppress the host source-tab navigation callback.");
        Assert.Contains("ShowAddedPowerInModifiedBrowser()", add, StringComparison.Ordinal);
    }
}
