using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Architecture)]
public sealed class SafeDeletionArchitectureTests
{
    [Fact]
    public void TypeDeletion_BlocksReferencesInsteadOfCascadingAssignments()
    {
        var window = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var unitTypeDelete = ProjectSource.ExtractMethod(window, "private async Task<bool> DeleteUnitTypeAsync");
        var techTypeDelete = ProjectSource.ExtractMethod(window, "private async Task<bool> DeleteTechTypeAsync");

        Assert.Contains("EnsureIdentifierCanBeDeletedAsync", unitTypeDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveUnitAssignments", unitTypeDelete, StringComparison.Ordinal);
        Assert.Contains("EnsureIdentifierCanBeDeletedAsync", techTypeDelete, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveTechnologyAssignments", techTypeDelete, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedDefinitionDeletion_UsesSharedReferenceCheckAndTransactions()
    {
        var window = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var commandDelete = ProjectSource.ExtractMethod(window, "private async Task<bool> DeleteProtoUnitCommandCoreAsync");
        var abilityDelete = ProjectSource.ExtractMethod(window, "private async Task ApplyAbilityManagerChangesAsync");
        var tacticsDelete = ProjectSource.ExtractMethod(window, "private async Task ApplyTacticsManagerChangesAsync");

        Assert.Contains("EnsureIdentifierCanBeDeletedAsync", commandDelete, StringComparison.Ordinal);
        Assert.Contains("ModDocumentPersistence.Execute", commandDelete, StringComparison.Ordinal);
        Assert.Contains("ReviewOrphanStringsAsync", commandDelete, StringComparison.Ordinal);
        Assert.Contains("EnsureIdentifierCanBeDeletedAsync", abilityDelete, StringComparison.Ordinal);
        Assert.Contains("ModDocumentPersistence.Execute", abilityDelete, StringComparison.Ordinal);
        Assert.Contains("ReviewOrphanStringsAsync", abilityDelete, StringComparison.Ordinal);
        Assert.Contains("EnsureIdentifierCanBeDeletedAsync", tacticsDelete, StringComparison.Ordinal);
        Assert.Contains("FileIntegrityTransaction.Execute", tacticsDelete, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanCleanup_IsExplicitAndKeepsEverythingByDefault()
    {
        var reviewWindow = ProjectSource.Read("Windows", "OrphanCleanupReviewWindow.cs");
        var protoWindow = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var technologyView = ProjectSource.Read("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("IsChecked = false", reviewWindow, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = false", reviewWindow, StringComparison.Ordinal);
        Assert.Contains("They are kept unless you explicitly select them", reviewWindow, StringComparison.Ordinal);
        Assert.Contains("ReviewOrphanStringsAsync", protoWindow, StringComparison.Ordinal);
        Assert.Contains("new OrphanCleanupReviewWindow", technologyView, StringComparison.Ordinal);
    }

    [Fact]
    public void Managers_UseTheSharedDetailedDeletionValidator()
    {
        foreach (var file in new[]
                 {
                     "UnitTypeManagerWindow.cs",
                     "TechTypeManagerWindow.cs",
                     "AbilitiesManagerWindow.cs",
                     "ProtoUnitCommandsManagerWindow.cs",
                     "TacticsManagerWindow.cs"
                 })
        {
            var source = ProjectSource.Read("Windows", file);
            Assert.Contains("_validateDeleteAsync", source, StringComparison.Ordinal);
            Assert.Contains("await _validateDeleteAsync(this, item.Name)", source, StringComparison.Ordinal);
        }

        var sharedDialog = ProjectSource.Read("Windows", "DeletionBlockedDialog.cs");
        Assert.Contains("{entityLabel} is still in use", sharedDialog, StringComparison.Ordinal);
        Assert.Contains("Width = 680", sharedDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void StringCleanup_UsesTheModWideDeletionSafetyIndex()
    {
        var window = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var filter = ProjectSource.ExtractMethod(window, "private string[] FilterUnreferencedStringIdsInCurrentMod");

        Assert.Contains("ModDeletionSafety.FindReferencedStringIds", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateFiles(gameDirectory, \"*.xml\"", filter, StringComparison.Ordinal);
    }

}
