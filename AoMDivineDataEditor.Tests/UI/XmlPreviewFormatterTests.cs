using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlPreviewFormatterTests
{
    [Fact]
    public void Beautify_IndentsNestedElementsEvenWhenParentContainsText()
    {
        const string xml = "<animfile><attachment>Helmet<include>greek\\helmet.xml</include></attachment><component>ModelComp<logic type=\"Tech\"><none /></logic></component></animfile>";

        var result = XmlPreviewFormatter.Beautify(xml);

        Assert.Contains($"<animfile>{Environment.NewLine}\t<attachment>Helmet{Environment.NewLine}\t\t<include>", result);
        Assert.Contains($"</include>{Environment.NewLine}\t</attachment>", result);
        Assert.Contains($"<component>ModelComp{Environment.NewLine}\t\t<logic type=\"Tech\">", result);
    }

    [Fact]
    public void Beautify_ReturnsMalformedXmlUnchanged()
    {
        const string malformed = "<animfile><anim></animfile>";

        Assert.Equal(malformed, XmlPreviewFormatter.Beautify(malformed));
    }

    [Theory]
    [InlineData("<animfile><attachment>Helmet<include>greek/helmet.xml</include></attachment><anim>Idle<file>idle.gr2</file></anim></animfile>")]
    [InlineData("<?xml version='1.0' encoding='utf-16'?><!--document--><soundset><future><nested/></future></soundset>")]
    public void BeautificationIsStableAcrossRepeatedViewerSavesAndRetainsTheDeclaration(string xml)
    {
        var once = XmlPreviewFormatter.Beautify(xml);
        Assert.Equal(once, XmlPreviewFormatter.Beautify(once));
        var original = System.Xml.Linq.XDocument.Parse(xml);
        var formatted = System.Xml.Linq.XDocument.Parse(once);
        Assert.Equal(original.Declaration?.Encoding, formatted.Declaration?.Encoding);
        Assert.Equal(original.Descendants().Select(element => element.Name), formatted.Descendants().Select(element => element.Name));
    }

    [Fact]
    public void BeautificationPreservesSpaceSensitiveAndProseMixedContent()
    {
        const string xml = "<animfile><attachment>Helmet<include>helmet.xml</include></attachment><preserved xml:space='preserve'> before <nested/> after </preserved><prose>before<em>inside</em>after</prose></animfile>";
        var before = System.Xml.Linq.XElement.Parse(xml, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var formatted = System.Xml.Linq.XElement.Parse(XmlPreviewFormatter.Beautify(xml), System.Xml.Linq.LoadOptions.PreserveWhitespace);
        Assert.True(System.Xml.Linq.XNode.DeepEquals(before.Element("preserved"), formatted.Element("preserved")));
        Assert.True(System.Xml.Linq.XNode.DeepEquals(before.Element("prose"), formatted.Element("prose")));
    }

    [Fact]
    public void PreparePreviewText_AddsExactlyOneDisplayOnlyTrailingLine()
    {
        const string xml = "<unit><name>Example</name></unit>";

        var result = XmlSyntaxEditorService.PreparePreviewText(xml);

        Assert.Equal(xml + Environment.NewLine, result);
        Assert.Equal(result, XmlSyntaxEditorService.PreparePreviewText(result));
        Assert.Equal(xml, System.Xml.Linq.XElement.Parse(result).ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
    }
}
