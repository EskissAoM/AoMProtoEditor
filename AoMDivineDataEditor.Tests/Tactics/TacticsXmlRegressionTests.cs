using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TacticsXmlRegressionTests
{
    [Fact]
    public void SaveProtoXml_ArmorOverrideNeverClosesAsArmor()
    {
        using var temp = new TestWorkspace(nameof(TacticsXmlRegressionTests));
        var path = temp.GetPath("test.tactics");
        var document = new XDocument(new XElement("tactics",
            new XElement("tactic", "Test",
                new XElement("armoroverride",
                    new XAttribute("type", "Hack"),
                    new XAttribute("value", "0.5")))));

        ProtoXmlHandler.SaveProtoXml(document, path);
        var xml = File.ReadAllText(path);

        var saved = XDocument.Parse(xml); // Proves the emitted file is well formed.
        var armorOverride = Assert.Single(saved.Descendants("armoroverride"));
        Assert.Equal("Hack", (string?)armorOverride.Attribute("type"));
        Assert.Equal("0.5", (string?)armorOverride.Attribute("value"));
        Assert.DoesNotContain("</armor>", xml);
    }

    [Fact]
    public void SaveProtoXml_UsesExactClosingTagForEveryArmorFamilyElement()
    {
        using var temp = new TestWorkspace(nameof(TacticsXmlRegressionTests));
        var path = temp.GetPath("armor.xml");
        var document = new XDocument(new XElement("unit",
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.2")),
            new XElement("directionalarmor", new XAttribute("type", "Pierce"), new XAttribute("value", "0.3")),
            new XElement("armoroverride", new XAttribute("type", "Crush"), new XAttribute("value", "0.4"))));

        ProtoXmlHandler.SaveProtoXml(document, path);
        var xml = File.ReadAllText(path);

        var saved = XDocument.Parse(xml);
        Assert.Single(saved.Descendants("armor"));
        Assert.Single(saved.Descendants("directionalarmor"));
        Assert.Single(saved.Descendants("armoroverride"));
        Assert.DoesNotContain("<armoroverride type=\"Crush\" value=\"0.4\"></armor>", xml);
    }

}
