using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyExternalDocumentMergePolicyTests
{
    [Fact]
    public void ExternalAndUnrelatedEditorChangesAreMerged()
    {
        var loaded = XDocument.Parse("""
            <techtreemods marker="old">
              <tech name="Local"><researchpoints>1</researchpoints></tech>
              <tech name="External"><effects /></tech>
            </techtreemods>
            """);
        var editor = new XDocument(loaded);
        editor.Descendants("tech").Single(tech => (string?)tech.Attribute("name") == "Local")
            .Element("researchpoints")!.Value = "2";
        var external = new XDocument(loaded);
        external.Root!.SetAttributeValue("marker", "external");
        external.Descendants("tech").Single(tech => (string?)tech.Attribute("name") == "External")
            .Element("effects")!.Add(new XElement("effect", new XAttribute("type", "Sound")));

        var result = TechnologyExternalDocumentMergePolicy.Merge(
            TechnologyExternalDocumentMergePolicy.CaptureTechnologyXml(loaded), editor, external);

        Assert.Equal("external", (string?)result.Document.Root!.Attribute("marker"));
        Assert.Equal("2", result.Document.Descendants("tech")
            .Single(tech => (string?)tech.Attribute("name") == "Local").Element("researchpoints")!.Value);
        Assert.NotNull(result.Document.Descendants("tech")
            .Single(tech => (string?)tech.Attribute("name") == "External").Descendants("effect").Single());
        Assert.Empty(result.ReplacedEditorChanges);
    }

    [Fact]
    public void ExternalVersionWinsWhenTheSameTechnologyChangedOnBothSides()
    {
        var loaded = XDocument.Parse("<techtreemods><tech name='T'><researchpoints>1</researchpoints></tech></techtreemods>");
        var editor = new XDocument(loaded);
        editor.Descendants("researchpoints").Single().Value = "2";
        var external = new XDocument(loaded);
        external.Descendants("researchpoints").Single().Value = "3";

        var result = TechnologyExternalDocumentMergePolicy.Merge(
            TechnologyExternalDocumentMergePolicy.CaptureTechnologyXml(loaded), editor, external);

        Assert.Equal("3", result.Document.Descendants("researchpoints").Single().Value);
        Assert.Equal(["T"], result.ReplacedEditorChanges);
    }

    [Fact]
    public void UnsavedDeletionIsKeptWhenDiskVersionDidNotChange()
    {
        var loaded = XDocument.Parse("<techtreemods><tech name='Keep'/><tech name='Delete'/></techtreemods>");
        var editor = new XDocument(loaded);
        editor.Descendants("tech").Single(tech => (string?)tech.Attribute("name") == "Delete").Remove();
        var external = new XDocument(loaded);
        external.Root!.Add(new XElement("future", "external"));

        var result = TechnologyExternalDocumentMergePolicy.Merge(
            TechnologyExternalDocumentMergePolicy.CaptureTechnologyXml(loaded), editor, external);

        Assert.DoesNotContain(result.Document.Descendants("tech"),
            tech => (string?)tech.Attribute("name") == "Delete");
        Assert.Equal("external", result.Document.Root!.Element("future")?.Value);
    }

    [Fact]
    public void TechnologyEditorOffersExternalTechtreeReloadAndShowsInvalidLoadedDocuments()
    {
        var source = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("ReloadExternallyChangedTechtreeAsync", source, StringComparison.Ordinal);
        Assert.Contains("Technologies changed outside the editor", source, StringComparison.Ordinal);
        Assert.Contains("The technology file cannot be edited safely", source, StringComparison.Ordinal);
        Assert.Contains("MergeTechs(_modDocument, _modified, overwrite: true, clone: false);", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([ProjectSource.RootPath, .. relativePath]));

}
