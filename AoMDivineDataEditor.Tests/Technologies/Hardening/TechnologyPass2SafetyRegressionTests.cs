using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass2SafetyRegressionTests
{
    [Fact]
    public void TechnologySaveUsesAValidatedCloneAndOneMultiFileTransaction()
    {
        var technologyCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("var candidateDocument = new XDocument(_modDocument)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("TechnologySaveValidator.Validate(candidateDocument", technologyCode, StringComparison.Ordinal);
        Assert.Contains("FileIntegrityTransaction.ExecuteAsync(paths, expectedStates", technologyCode, StringComparison.Ordinal);
        Assert.Contains("SaveAbilityXmlDocument(new XDocument(candidateDocument)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("_saveStringsAsync!(stringUpdatesToSave, stringRemovalsToSave)", technologyCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyDirtyComparisonIgnoresFormattingWhitespace()
    {
        var formatted = XDocument.Parse("""
            <techtreemods>
              <tech name="T">
                <status>UNOBTAINABLE</status>
                <researchpoints>0</researchpoints>
              </tech>
            </techtreemods>
            """, LoadOptions.PreserveWhitespace).Root!.Element("tech")!;
        var compact = XElement.Parse("<tech name=\"T\"><status>UNOBTAINABLE</status><researchpoints>0</researchpoints></tech>");

        Assert.Equal(
            TechnologyEditorView.SerializeTechnologyForComparison(formatted),
            TechnologyEditorView.SerializeTechnologyForComparison(compact));
    }

    [Fact]
    public void DirtyStateIsClearedOnlyForTheSnapshotThatActuallyCommitted()
    {
        var technologyCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("var savedTechnologyXml = (candidateDocument.Root?", technologyCode, StringComparison.Ordinal);
        Assert.Contains("var stringUpdatesToSave = new Dictionary<string, string>", technologyCode, StringComparison.Ordinal);
        Assert.Contains("foreach (var (name, xml) in savedTechnologyXml)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("RecalculateDirtyState();", technologyCode, StringComparison.Ordinal);
        Assert.Contains("newer edits remain unsaved", technologyCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyAndStringFilesAreFingerprintProtected()
    {
        var technologyCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");
        var transactionCode = ReadSource("Classes", "FileIntegrityTransaction.cs");

        Assert.Contains("CaptureState(_modTechtreePath)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("CaptureState(_modStringsPath)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("EnsureExpectedStatesMatch", transactionCode, StringComparison.Ordinal);
        Assert.Contains("changed outside the editor", transactionCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologySaveConfirmationSurvivesHarmlessDelayedEditorCallbacks()
    {
        var technologyCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("ShowSavedStatus(_dirty", technologyCode, StringComparison.Ordinal);
        Assert.Contains("else if (_statusMessage.Text == \"Modified\")", technologyCode, StringComparison.Ordinal);
        Assert.Contains("_savedStatusGeneration == generation", technologyCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnologyIconPreviewRefreshesAfterTheLiveXmlValueChanges()
    {
        var technologyCode = ReadSource("Windows", "TechnologyEditorView.axaml.cs");
        var iconEditorStart = technologyCode.IndexOf("private void AddIconEditor", StringComparison.Ordinal);
        var iconEditorEnd = technologyCode.IndexOf("private void AddChipListEditor", iconEditorStart, StringComparison.Ordinal);
        var iconEditorCode = technologyCode[iconEditorStart..iconEditorEnd];

        Assert.True(
            iconEditorCode.IndexOf("icon.Value = value", StringComparison.Ordinal) <
            iconEditorCode.IndexOf("previewControl.ShowOptionsAsync", StringComparison.Ordinal));
        Assert.Contains("ReferenceEquals(_current, owningTechnology)", iconEditorCode, StringComparison.Ordinal);
        Assert.DoesNotContain("editor.FullValueChanged", iconEditorCode, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([ProjectSource.RootPath, .. relativePath]));

}
