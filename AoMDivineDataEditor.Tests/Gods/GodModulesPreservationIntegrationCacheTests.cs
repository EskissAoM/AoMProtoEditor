using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class GodModulesPreservationIntegrationCacheTests
{
    private static readonly UnicodeEncoding Utf16WithBom = new(
        bigEndian: false,
        byteOrderMark: true,
        throwOnInvalidBytes: true);

    [Fact]
    public void CultureSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml()
    {
        using var workspace = new TestWorkspace(nameof(CultureSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml));
        var path = WriteUtf16(workspace, CultureCatalog.ModFileName, """
            <?xml version="1.0" encoding="utf-16"?>
            <?editor keep?>
            <culturesmods custom="root-value">
              <!-- root comment -->
              <culture legacy="yes">
                <name>MyCulture</name>
                <displaynameid>STR_CULTURE_MYCULTURE</displaynameid>
                <compendiumtopic><!-- inside topic -->old<?topic keep?></compendiumtopic>
                <future><payload answer="42" /></future>
              </culture>
            </culturesmods>
            """);
        var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.Cultures);
        var culture = CultureCatalog.FindDefinition(prepared.Document.Root!, "MyCulture")!;

        CultureCatalog.SetField(culture, "compendiumtopic", "new-topic");
        ModDocumentPersistence.Execute(
            [prepared],
            null,
            null,
            () => ProtoEditorWindow.SaveCultureXmlDocument(prepared.Document, path));

        var saved = AssertUtf16CrLf(path);
        var entry = CultureCatalog.FindDefinition(saved.Root!, "MyCulture")!;
        Assert.Equal("root-value", (string?)saved.Root!.Attribute("custom"));
        Assert.Equal("yes", (string?)entry.Attribute("legacy"));
        Assert.Equal("new-topic", CultureCatalog.ReadField(entry, "compendiumtopic"));
        Assert.NotNull(entry.Element("future")?.Element("payload"));
        AssertTrivia(saved, entry.Element("compendiumtopic")!, "root comment", "inside topic", "editor", "topic");
    }

    [Fact]
    public void MajorGodSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml()
    {
        using var workspace = new TestWorkspace(nameof(MajorGodSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml));
        var path = WriteUtf16(workspace, MajorGodCatalog.ModFileName, """
            <?xml version="1.0" encoding="utf-16"?>
            <?editor keep?>
            <civsmods custom="root-value">
              <!-- root comment -->
              <civ legacy="yes">
                <name>MyGod</name>
                <statsid>SP</statsid>
                <titleid><!-- inside title -->STR_CIV_MYGOD_T<?title keep?></titleid>
                <future><payload answer="42" /></future>
              </civ>
            </civsmods>
            """);
        var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.MajorGods);
        var majorGod = MajorGodCatalog.Find(prepared.Document, "MyGod")!;

        MajorGodCatalog.SetPass1Value(majorGod, "titleid", "STR_CIV_MYGOD_NEW_T");
        ModDocumentPersistence.Execute(
            [prepared],
            null,
            null,
            () => ProtoEditorWindow.SaveAbilityXmlDocument(prepared.Document, path));

        var saved = AssertUtf16CrLf(path);
        var entry = MajorGodCatalog.Find(saved, "MyGod")!;
        Assert.Equal("root-value", (string?)saved.Root!.Attribute("custom"));
        Assert.Equal("yes", (string?)entry.Attribute("legacy"));
        Assert.Equal("STR_CIV_MYGOD_NEW_T", MajorGodCatalog.FindDirectChild(entry, "titleid")?.Value.Trim());
        Assert.NotNull(entry.Element("future")?.Element("payload"));
        AssertTrivia(saved, entry.Element("titleid")!, "root comment", "inside title", "editor", "title");
    }

    [Fact]
    public void MinorGodSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml()
    {
        using var workspace = new TestWorkspace(nameof(MinorGodSavePreservesEncodingNewlinesCommentsInstructionsAndUnknownXml));
        var path = WriteUtf16(workspace, MinorGodCatalog.ModFileName, """
            <?xml version="1.0" encoding="utf-16"?>
            <?editor keep?>
            <minorgodsmods custom="root-value">
              <!-- root comment -->
              <MythicAgeMyGod legacy="yes" portraitfilename="old.png" portraiticonfilename="icon.png">
                <!-- inside minor god -->
                <?minor keep?>
                <future><payload answer="42" /></future>
              </MythicAgeMyGod>
            </minorgodsmods>
            """);
        var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.MinorGods);
        var minorGod = MinorGodCatalog.FindDefinition(prepared.Document.Root!, "MythicAgeMyGod")!;

        MinorGodCatalog.SetAttribute(minorGod, MinorGodCatalog.PortraitAttribute, "new.png");
        ModDocumentPersistence.Execute(
            [prepared],
            null,
            null,
            () => ProtoEditorWindow.SaveMinorGodXmlDocument(prepared.Document, path));

        var saved = AssertUtf16CrLf(path);
        var entry = MinorGodCatalog.FindDefinition(saved.Root!, "MythicAgeMyGod")!;
        Assert.Equal("root-value", (string?)saved.Root!.Attribute("custom"));
        Assert.Equal("yes", (string?)entry.Attribute("legacy"));
        Assert.Equal("new.png", MinorGodCatalog.ReadAttribute(entry, MinorGodCatalog.PortraitAttribute));
        Assert.NotNull(entry.Element("future")?.Element("payload"));
        AssertTrivia(saved, entry, "root comment", "inside minor god", "editor", "minor");
    }

    [Fact]
    public void OpenEditorsAcceptFreshCrossModuleCatalogsWithoutBeingRecreated()
    {
        AvaloniaTestHost.EnsureStarted();
        AvaloniaTestHost.Run(() =>
        {
            var technologyEditor = new TechnologyEditorView(
                originalBarDocuments: null,
                baseGameplayDirectory: null,
                modTechtreePath: null,
                majorGodNames: ["OldGod"]);
            technologyEditor.UpdateMajorGodNames(["NewGod", "AnotherGod"]);
            Assert.Equal(
                ["AnotherGod", "NewGod"],
                GetPrivateField<IReadOnlyList<string>>(technologyEditor, "_majorGodNames"));

            var majorGodEditor = new MajorGodEditorView(
                [],
                null,
                technologyNames: ["OldTech"],
                technologyFlagNames: ["OldFlag"],
                techTypeNames: ["OldType"]);
            majorGodEditor.UpdateTechnologyCatalogs(
                ["NewTech", "OtherTech"],
                ["NewFlag"],
                ["NewType"]);

            var ageTech = majorGodEditor.FindControl<AutoCompleteBox>("_ageTechBox")!;
            Assert.Equal(["NewTech", "OtherTech"], ageTech.ItemsSource!.Cast<string>());
            Assert.Equal(["NewFlag"], GetPrivateField<IReadOnlyList<string>>(majorGodEditor, "_technologyFlagNames"));
            Assert.Equal(["NewType"], GetPrivateField<IReadOnlyList<string>>(majorGodEditor, "_techTypeNames"));
        });
    }

    [Fact]
    public void SuccessfulCrossModuleSavesInvalidateAndPushCatalogsToOpenEditors()
    {
        var source = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var majorGodInitialization = ProjectSource.ExtractBetween(
            source,
            "private void EnsureMajorGodView()",
            "private void MajorGodView_BrowserStateChanged");
        var majorGodSaved = ProjectSource.ExtractBetween(
            source,
            "private void MajorGodView_Saved",
            "private GameIdentifierReferenceMigrationPlan PrepareMajorGodReferenceMigration");
        var technologySaved = ProjectSource.ExtractBetween(
            source,
            "private Task AdoptSavedTechnologyLifecycleAsync",
            "private bool HasCustomMinorGodForTechnology");

        Assert.Contains("_majorGodView.Saved += MajorGodView_Saved", majorGodInitialization, StringComparison.Ordinal);
        Assert.Contains("InvalidateSuggestionCaches()", majorGodSaved, StringComparison.Ordinal);
        Assert.Contains("_technologyView?.UpdateMajorGodNames", majorGodSaved, StringComparison.Ordinal);
        Assert.Contains("InvalidateSuggestionCaches(includeTechNames: true)", technologySaved, StringComparison.Ordinal);
        Assert.Contains("_majorGodView?.UpdateTechnologyCatalogs", technologySaved, StringComparison.Ordinal);
    }

    private static string WriteUtf16(TestWorkspace workspace, string fileName, string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var path = workspace.GetPath(fileName);
        File.WriteAllBytes(path, Utf16WithBom.GetPreamble().Concat(Utf16WithBom.GetBytes(normalized)).ToArray());
        return path;
    }

    private static XDocument AssertUtf16CrLf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan().StartsWith(Utf16WithBom.GetPreamble()));
        var text = Utf16WithBom.GetString(bytes, Utf16WithBom.GetPreamble().Length, bytes.Length - Utf16WithBom.GetPreamble().Length);
        Assert.Contains("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        return XDocument.Parse(text, LoadOptions.PreserveWhitespace);
    }

    private static void AssertTrivia(
        XDocument document,
        XElement editedElement,
        string rootComment,
        string innerComment,
        string documentInstruction,
        string innerInstruction)
    {
        Assert.Contains(document.DescendantNodes().OfType<XComment>(), comment =>
            comment.Value.Contains(rootComment, StringComparison.Ordinal));
        Assert.Contains(editedElement.DescendantNodesAndSelf().OfType<XComment>(), comment =>
            comment.Value.Contains(innerComment, StringComparison.Ordinal));
        Assert.Contains(document.Nodes().OfType<XProcessingInstruction>(), instruction =>
            instruction.Target.Equals(documentInstruction, StringComparison.Ordinal));
        Assert.Contains(editedElement.DescendantNodesAndSelf().OfType<XProcessingInstruction>(), instruction =>
            instruction.Target.Equals(innerInstruction, StringComparison.Ordinal));
    }

    private static T GetPrivateField<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                   ?.GetValue(instance)
               ?? throw new MissingFieldException(instance.GetType().FullName, name));
}
