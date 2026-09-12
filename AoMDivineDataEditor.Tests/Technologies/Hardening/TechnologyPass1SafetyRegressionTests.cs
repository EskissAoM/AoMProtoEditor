using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass1SafetyRegressionTests
{
    [Fact]
    public void ImportedEffectsOnlyReceiveDefaultsInsideExplicitInitializationScope()
    {
        var code = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("_effectsPendingDefaultInitialization.Add(effect)", code, StringComparison.Ordinal);
        Assert.Contains("_initializingEffectDefaults = initializeDefaults", code, StringComparison.Ordinal);
        Assert.Contains("if (!_initializingEffectDefaults || GetCaseInsensitiveAttribute(effect, attributeName) != null) return", code, StringComparison.Ordinal);
        Assert.Contains("if (!_initializingEffectDefaults) return;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsModifiedTab && TechnologyDataEffectRules.Normalize", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsModifiedTab && TechnologyOnHitEffectRules.Normalize", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedTextEffectsAndScalarPrerequisitesAreCreatedLazily()
    {
        var code = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("string.IsNullOrWhiteSpace(stringId) && _initializingEffectDefaults", code, StringComparison.Ordinal);
        Assert.Contains("attribute == null && _initializingEffectDefaults", code, StringComparison.Ordinal);
        Assert.Contains("entries.Add(new XElement(childName, prereq.Value.Trim()))", code, StringComparison.Ordinal);
        Assert.Contains("prereq.RemoveNodes();\n                    prereq.Add(activeEntry);", code.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyDataAttributesAreDisplayedButOnlyMigratedAfterExplicitEditing()
    {
        var code = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("if (!_initializingEffectDefaults) return;", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"power\")?.Value.Trim() ?? \"\"", code, StringComparison.Ordinal);
        Assert.Contains("RemoveCaseInsensitiveAttribute(effect, \"power\")", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"active\")?.Value.Trim() ?? \"\"", code, StringComparison.Ordinal);
        Assert.Contains("RemoveCaseInsensitiveAttribute(effect, \"active\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeModDocumentBlocksEditingSavingAndTopLevelMutationCommands()
    {
        var editorCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");
        var windowCode = ReadSource("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("TechnologyDocumentSafety.ValidateModDocument(document)", editorCode, StringComparison.Ordinal);
        Assert.Contains("if (_modDocumentSafetyError != null)", editorCode, StringComparison.Ordinal);
        Assert.Contains("return false;", editorCode, StringComparison.Ordinal);
        Assert.Contains("public bool CanModifyTechnologyDocument => _modDocumentSafetyError == null", editorCode, StringComparison.Ordinal);
        Assert.Contains("_technologyView!.CanModifyTechnologyDocument", windowCode, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = isModified && technologyModificationsAllowed", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAnEffectPreservesTheExistingDirectOrWrappedShape()
    {
        var code = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("if (container != null)\n            {\n                container.Add(effect);", code.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("tech.Elements().Any(e => e.Name.LocalName.Equals(\"effect\"", code, StringComparison.Ordinal);
        Assert.Contains("tech.Add(effect);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DirtyStateIsReconciledAgainstSavedTechnologyXmlAndIgnoresNoOpControlEvents()
    {
        var code = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("_savedModifiedTechnologyXml", code, StringComparison.Ordinal);
        Assert.Contains("TechnologyHasUnsavedChanges(_currentOriginalName)", code, StringComparison.Ordinal);
        Assert.Contains("RecalculateDirtyState();", code, StringComparison.Ordinal);
        Assert.Contains("if (newName.Equals(_currentOriginalName, StringComparison.Ordinal)) return;", code, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(lastCommittedValue, StringComparison.OrdinalIgnoreCase)", code, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([ProjectSource.RootPath, .. relativePath]));

}
