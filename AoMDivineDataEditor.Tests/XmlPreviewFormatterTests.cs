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
}
