using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorXmlOrderPolicyTests
{
    private static readonly IReadOnlyDictionary<string, int> Ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    { ["name"] = 0, ["icon"] = 1, ["item"] = 2 };

    [Theory]
    [InlineData("tech")]
    [InlineData("power")]
    [InlineData("civ")]
    public void SharedOrderingKeepsFieldCommentsRepeatedPayloadAndIdempotence(string root)
    {
        var entry = XElement.Parse($"<{root}><item id='2'/><!--icon note--><icon>x</icon><item id='1'/><name>N</name></{root}>");
        var items = entry.Elements("item").ToArray();
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal(new[] { "name", "icon", "item", "item" }, entry.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(items, entry.Elements("item"));
        Assert.IsType<XComment>(entry.Element("icon")!.PreviousNode);
        var shape = entry.ToString(SaveOptions.DisableFormatting);
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal(shape, entry.ToString(SaveOptions.DisableFormatting));
    }

    [Theory]
    [InlineData("<future/>")]
    [InlineData("<x:icon xmlns:x='urn:future'/>")]
    [InlineData("<?future keep?>")]
    [InlineData("\n\n<!--section-->\n\n")]
    public void OpaqueAndAmbiguousSectionBoundariesAreNotCrossed(string barrier)
    {
        var entry = XElement.Parse($"<power><icon>x</icon>{barrier}<name>N</name></power>", LoadOptions.PreserveWhitespace);
        var shape = entry.ToString(SaveOptions.DisableFormatting);
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal(shape, entry.ToString(SaveOptions.DisableFormatting));
    }

    [Theory]
    [InlineData("<civ xml:space='preserve'><icon>x</icon><name>N</name></civ>")]
    [InlineData("<civ>mixed<icon>x</icon><name>N</name></civ>")]
    [InlineData("<civ><![CDATA[ ]]><icon>x</icon><name>N</name></civ>")]
    public void ProtectedContentIsNotReordered(string xml)
    {
        var entry = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var shape = entry.ToString(SaveOptions.DisableFormatting);
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal(shape, entry.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void InheritedPreservationAndExplicitDefaultAreRespected()
    {
        var parent = XElement.Parse("<root xml:space='preserve'><power><icon>x</icon><name>N</name></power></root>");
        var entry = parent.Element("power")!;
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal("icon", entry.Elements().First().Name.LocalName);
        entry.SetAttributeValue(XNamespace.Xml + "space", "default");
        EditorXmlOrderPolicy.Normalize(entry, Ranks);
        Assert.Equal("name", entry.Elements().First().Name.LocalName);
    }
}
