using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitXmlOrderPolicyTests
{
    [Fact]
    public void NormalizeForEditor_PlacesCommandsTacticsAndActionsInCanonicalSections()
    {
        var unit = XElement.Parse("""
            <unit name="Example"><protoaction><name>Attack</name><type>Attack</type></protoaction><culture>Greek</culture><command row="0">DoWork</command><unknown>kept</unknown><tactics>example.tactics</tactics><optionalcommand>Optional</optionalcommand></unit>
            """, LoadOptions.PreserveWhitespace);

        ProtoUnitXmlOrderPolicy.NormalizeForEditor(unit);

        Assert.Equal(
            ["culture", "unknown", "command", "optionalcommand", "tactics", "protoaction"],
            unit.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public void NormalizeForEditor_PutsStructuredSiblingsOnSeparateIndentedLines()
    {
        var unit = XElement.Parse("""
            <unit name="Example"><protoaction><name>Attack</name><rate type="All">1</rate></protoaction><protoaction><name>Heal</name><active>0</active></protoaction></unit>
            """, LoadOptions.PreserveWhitespace);

        ProtoUnitXmlOrderPolicy.NormalizeForEditor(unit);
        var xml = unit.ToString(SaveOptions.DisableFormatting);

        Assert.Contains($"{Environment.NewLine}  <protoaction>{Environment.NewLine}    <name>Attack</name>", xml, StringComparison.Ordinal);
        Assert.Contains($"</protoaction>{Environment.NewLine}  <protoaction>", xml, StringComparison.Ordinal);
        Assert.Contains($"<name>Heal</name>{Environment.NewLine}    <active>0</active>", xml, StringComparison.Ordinal);
        Assert.EndsWith($"{Environment.NewLine}</unit>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeForEditor_PreservesMeaningfulMixedText()
    {
        var unit = XElement.Parse("<unit name=\"Example\"><custom>prefix<child>value</child></custom><protoaction><name>Attack</name></protoaction></unit>");

        ProtoUnitXmlOrderPolicy.NormalizeForEditor(unit);

        Assert.Equal("prefix", unit.Element("custom")?.Nodes().OfType<XText>().Single().Value);
        Assert.Equal("value", unit.Element("custom")?.Element("child")?.Value);
    }
}
