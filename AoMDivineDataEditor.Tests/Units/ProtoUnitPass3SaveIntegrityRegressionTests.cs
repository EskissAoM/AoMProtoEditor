using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitPass3SaveIntegrityRegressionTests
{
    [Fact]
    public void ProtoAndStringsCaptureLoadedFileFingerprints()
    {
        var code = ReadProtoEditorSource();

        Assert.Contains("_loadedModXmlFileState = stateAfterRead;", code, StringComparison.Ordinal);
        Assert.Contains("_loadedModStringsFileState = stateAfterRead;", code, StringComparison.Ordinal);
        Assert.Contains("stateBeforeRead != stateAfterRead", code, StringComparison.Ordinal);
        Assert.Contains("PreservedTextEncoding.Decode(File.ReadAllBytes(path))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoUnitSaveValidatesExpectedStatesAndPreflightsMigrationsBeforeWriting()
    {
        var code = ReadProtoEditorSource();
        var saveStart = code.IndexOf("private async Task SaveCurrentUnitCoreAsync", StringComparison.Ordinal);
        var saveEnd = code.IndexOf("private async void ProtounitEditView_Click", saveStart, StringComparison.Ordinal);
        Assert.True(saveStart >= 0 && saveEnd > saveStart);
        var saveCode = code[saveStart..saveEnd];

        var preflightIndex = saveCode.IndexOf("var migrationPreflight = GameIdentifierReferenceMigrationPlan.Prepare", StringComparison.Ordinal);
        var transactionIndex = saveCode.IndexOf("FileIntegrityTransaction.Execute(", StringComparison.Ordinal);
        var firstWriteIndex = saveCode.IndexOf("SavePendingUnitRenames();", StringComparison.Ordinal);

        Assert.True(preflightIndex >= 0 && preflightIndex < transactionIndex);
        Assert.True(transactionIndex >= 0 && transactionIndex < firstWriteIndex);
        Assert.Contains("AddExpectedState(expectedStates, _modFilePath, _loadedModXmlFileState)", saveCode, StringComparison.Ordinal);
        Assert.Contains("AddExpectedState(expectedStates, GetCurrentModStringsPath(), _loadedModStringsFileState)", saveCode, StringComparison.Ordinal);
        Assert.Contains("transactionPaths,", saveCode, StringComparison.Ordinal);
        Assert.Contains("expectedStates,", saveCode, StringComparison.Ordinal);
    }

    [Fact]
    public void StringSnapshotWritesRequireTheLoadedFingerprint()
    {
        var code = ReadProtoEditorSource();
        var methodStart = code.IndexOf("private void SaveCurrentModStringEntries(", StringComparison.Ordinal);
        var methodEnd = code.IndexOf("private static void AtomicWriteAllBytes", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var methodCode = code[methodStart..methodEnd];

        Assert.Contains("var expectedState = expectedFileState ?? _loadedModStringsFileState", methodCode, StringComparison.Ordinal);
        Assert.Contains("if (expectedState == null)", methodCode, StringComparison.Ordinal);
        Assert.Contains("FileIntegrityTransaction.Execute(", methodCode, StringComparison.Ordinal);
        Assert.Contains("[Path.GetFullPath(path)] = expectedState", methodCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicXmlWriterSkipsAByteIdenticalReplacement()
    {
        var code = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Classes", "PreservedXmlFile.cs"));

        Assert.Contains("SequenceEqual(output)", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("SequenceEqual", StringComparison.Ordinal) <
            code.IndexOf("File.Move(temporaryPath, fullPath, overwrite: true)", StringComparison.Ordinal));
    }

    [Fact]
    public void TacticsManagerCommitsFilesAndReferencesAsOneGuardedTransaction()
    {
        var code = ReadProtoEditorSource();
        var methodStart = code.IndexOf("private async Task ApplyTacticsManagerChangesAsync", StringComparison.Ordinal);
        var methodEnd = code.IndexOf("private void RenameTacticsReferences", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var methodCode = code[methodStart..methodEnd];

        Assert.Contains("transactionPaths.AddRange(pathsToCreate)", methodCode, StringComparison.Ordinal);
        Assert.Contains("transactionPaths.AddRange(pathsToDelete)", methodCode, StringComparison.Ordinal);
        Assert.Contains("FileIntegrityTransaction.Execute(", methodCode, StringComparison.Ordinal);
        Assert.Contains("CaptureExpectedEditorFileStates(transactionPaths, preparedStates)", methodCode, StringComparison.Ordinal);
        Assert.Contains("SaveXmlDocumentAtomic(candidateProtoDocument", methodCode, StringComparison.Ordinal);
        Assert.Contains("SaveXmlDocumentAtomic(candidateTechtreeDocument", methodCode, StringComparison.Ordinal);
        Assert.DoesNotContain("catch { }", methodCode, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalSavePreflightsPendingUnitWorkBeforeSavingAnotherModule()
    {
        var code = ReadProtoEditorSource();
        var methodStart = code.IndexOf("private async void Save_Click", StringComparison.Ordinal);
        var methodEnd = code.IndexOf("private async Task SaveCurrentUnitCoreAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var methodCode = code[methodStart..methodEnd];

        var preflightIndex = methodCode.IndexOf("PreflightPendingUnitSave();", StringComparison.Ordinal);
        var majorGodSaveIndex = methodCode.IndexOf("_majorGodView.SaveAsync()", StringComparison.Ordinal);
        var technologySaveIndex = methodCode.IndexOf("_technologyView.SaveAsync()", StringComparison.Ordinal);
        Assert.True(preflightIndex >= 0 && preflightIndex < majorGodSaveIndex);
        Assert.True(preflightIndex < technologySaveIndex);
    }

    [Fact]
    public void ModSwitchOnlyPromptsForSaveableDocumentChanges()
    {
        var code = ReadProtoEditorSource();
        var methodStart = code.IndexOf("private async Task<bool> PromptUnsavedChangesAsync", StringComparison.Ordinal);
        var methodEnd = code.IndexOf("private string? GetLocalModsDirectory", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var methodCode = code[methodStart..methodEnd];

        Assert.Contains("_modXmlDoc != null", methodCode, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(_modFilePath)", methodCode, StringComparison.Ordinal);
        Assert.Contains("_dirtyUnitNames.Count > 0", methodCode, StringComparison.Ordinal);
        Assert.Contains("_majorGodView?.IsDirty", methodCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalChangesOfferReloadInsteadOfOverwriteAcrossUnitEditors()
    {
        var code = ReadProtoEditorSource();

        Assert.Contains("ReloadExternallyChangedUnitFilesAsync", code, StringComparison.Ordinal);
        Assert.Contains("NamedXmlExternalDocumentMergePolicy.Merge(", code, StringComparison.Ordinal);
        Assert.Contains("confirmButtonText: \"Reload\"", code, StringComparison.Ordinal);
        Assert.Contains("TacticsDocumentSaveOutcome.Reloaded", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Save and overwrite those external changes?", code, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs"));

}
