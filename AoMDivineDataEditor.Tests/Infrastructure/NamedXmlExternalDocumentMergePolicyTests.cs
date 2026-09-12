using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class NamedXmlExternalDocumentMergePolicyTests
{
    private static string? Name(XElement element) => element.Attribute("name")?.Value;

    [Fact]
    public void Merge_KeepsIndependentLocalAndExternalChanges()
    {
        var loaded = XDocument.Parse("<protomods><unit name='A'><hp>1</hp></unit><unit name='B'><hp>1</hp></unit></protomods>");
        var editor = XDocument.Parse("<protomods><unit name='A'><hp>2</hp></unit><unit name='B'><hp>1</hp></unit></protomods>");
        var external = XDocument.Parse("<protomods><unit name='A'><hp>1</hp></unit><unit name='B'><hp>3</hp></unit></protomods>");

        var result = NamedXmlExternalDocumentMergePolicy.Merge(loaded, editor, external, "unit", Name);

        Assert.Equal("2", result.Document.Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "A").Element("hp")!.Value);
        Assert.Equal("3", result.Document.Root!.Elements("unit").Single(unit => (string?)unit.Attribute("name") == "B").Element("hp")!.Value);
        Assert.Empty(result.ReplacedEditorChanges);
    }

    [Fact]
    public void Merge_UsesExternalRecordForSameNameConflict()
    {
        var loaded = XDocument.Parse("<protomods><unit name='A'><hp>1</hp></unit></protomods>");
        var editor = XDocument.Parse("<protomods><unit name='A'><hp>2</hp></unit></protomods>");
        var external = XDocument.Parse("<protomods><!--keep--><unit name='A'><hp>3</hp></unit></protomods>");

        var result = NamedXmlExternalDocumentMergePolicy.Merge(loaded, editor, external, "unit", Name);

        Assert.Equal("3", result.Document.Root!.Element("unit")!.Element("hp")!.Value);
        Assert.Contains("A", result.ReplacedEditorChanges);
        Assert.Contains(result.Document.Root.Nodes(), node => node is XComment comment && comment.Value == "keep");
    }

    [Fact]
    public void Merge_PreservesIndependentLocalAdditionAndDeletion()
    {
        var loaded = XDocument.Parse("<protomods><unit name='A'/><unit name='B'/></protomods>");
        var editor = XDocument.Parse("<protomods><unit name='B'/><unit name='C'/></protomods>");
        var external = XDocument.Parse("<protomods><unit name='A'/><unit name='B'/><unit name='D'/></protomods>");

        var result = NamedXmlExternalDocumentMergePolicy.Merge(loaded, editor, external, "unit", Name);
        var names = result.Document.Root!.Elements("unit").Select(Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("A", names);
        Assert.Contains("B", names);
        Assert.Contains("C", names);
        Assert.Contains("D", names);
    }
}
