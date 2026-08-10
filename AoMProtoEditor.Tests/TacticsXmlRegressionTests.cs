using System.Xml.Linq;
using CryBarEditor.Classes;
using Xunit;

namespace AoMProtoEditor.Tests;

public sealed class TacticsXmlRegressionTests
{
    [Fact]
    public void SaveProtoXml_ArmorOverrideNeverClosesAsArmor()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "test.tactics");
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
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "armor.xml");
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AoMProtoEditor.Tests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
