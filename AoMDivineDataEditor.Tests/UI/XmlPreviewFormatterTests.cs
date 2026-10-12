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
