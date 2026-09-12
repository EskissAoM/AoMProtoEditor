using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitStatsXmlRegressionTests
{
    [Fact]
    public void CarryCapacity_ZeroValuesAreRemovedFromXml()
    {
        var unit = new XElement("unit",
            new XElement("carrycapacity", new XAttribute("resourcetype", "Food"), "12"));

        ProtoXmlHandler.SetResourceMapEntries(
            unit,
            "carrycapacity",
            [
                ("Food", "0", ""),
                ("Wood", "0.0", "2"),
                ("Gold", "", "")
            ],
            omitZeroValues: true);

        Assert.Empty(unit.Elements("carrycapacity"));
    }

    [Fact]
    public void CarryCapacity_NonZeroValueKeepsItsDropOffMultiplier()
    {
        var unit = new XElement("unit");

        ProtoXmlHandler.SetResourceMapEntries(
            unit,
            "carrycapacity",
            [
                ("Food", "15", "1.5"),
                ("Wood", "0", "2")
            ],
            omitZeroValues: true);

        var capacity = Assert.Single(unit.Elements("carrycapacity"));
        Assert.Equal("Food", (string?)capacity.Attribute("resourcetype"));
        Assert.Equal("15", capacity.Value);
        Assert.Equal("1.5", (string?)capacity.Attribute("dropoffmultiplier"));
    }

    [Fact]
    public void OtherResourceMaps_CanStillPersistZeroValues()
    {
        var unit = new XElement("unit");

        ProtoXmlHandler.SetResourceMapEntries(
            unit,
            "initialresource",
            [("Food", "0", "")]);

        var resource = Assert.Single(unit.Elements("initialresource"));
        Assert.Equal("0", resource.Value);
    }

    [Fact]
    public void InitialResource_ZeroCanBeExplicitlyOmittedByStatsSerializerPolicy()
    {
        var unit = new XElement("unit");

        ProtoXmlHandler.SetResourceMapEntries(
            unit,
            "initialresource",
            [("Food", "0", "")],
            omitZeroValues: true);

        Assert.Empty(unit.Elements("initialresource"));
    }

    [Fact]
    public void Costs_ZeroValuesAreRemovedWhileNonZeroValuesRemain()
    {
        var unit = new XElement("unit",
            new XElement("cost", new XAttribute("resourcetype", "Favor"), "10"));

        ProtoXmlHandler.SetCostEntries(
            unit,
            [
                ("Food", "0"),
                ("Wood", "0.000"),
                ("Gold", "25"),
                ("Favor", "")
            ]);

        var cost = Assert.Single(unit.Elements("cost"));
        Assert.Equal("Gold", (string?)cost.Attribute("resourcetype"));
        Assert.Equal("25", cost.Value);
    }

    [Fact]
    public void Armor_ZeroValuesAreRemovedWhileNonZeroValuesRemain()
    {
        var unit = new XElement("unit",
            new XElement("armor", new XAttribute("type", "Divine"), new XAttribute("value", "0.5")));

        ProtoXmlHandler.SetArmorEntries(
            unit,
            [
                ("Hack", "0"),
                ("Pierce", "0.000"),
                ("Crush", "0.25")
            ]);

        var armor = Assert.Single(unit.Elements("armor"));
        Assert.Equal("Crush", (string?)armor.Attribute("type"));
        Assert.Equal("0.25", (string?)armor.Attribute("value"));
    }

    [Fact]
    public void StatsPreserver_DoesNotRestoreManagedArmorRemovedByZeroRule()
    {
        var original = new XElement("unit",
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.2")));
        var updated = new XElement("unit");

        ProtoXmlHandler.SetArmorEntries(updated, [("Hack", "0")]);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        Assert.Empty(updated.Elements("armor"));
    }

    [Fact]
    public void StatsPreserver_DoesNotRestoreInitialResourceRemovedByZeroRule()
    {
        var original = new XElement("unit",
            new XElement("initialresource", new XAttribute("resourcetype", "Food"), "25"));
        var updated = new XElement("unit");

        ProtoXmlHandler.SetResourceMapEntries(
            updated,
            "initialresource",
            [("Food", "0", "")],
            omitZeroValues: true);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        Assert.Empty(updated.Elements("initialresource"));
    }

    [Fact]
    public void StatsPreserver_KeepsUnknownAndDuplicateCostAndArmorEntries()
    {
        var original = new XElement("unit",
            new XElement("cost", new XAttribute("resourcetype", "Food"), "10"),
            new XElement("cost", new XAttribute("resourcetype", "Food"), "20"),
            new XElement("cost", new XAttribute("resourcetype", "CustomResource"), "7"),
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.1")),
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.2")),
            new XElement("armor", new XAttribute("type", "Divine"), new XAttribute("value", "0.5")));
        var updated = new XElement("unit",
            new XElement("cost", new XAttribute("resourcetype", "Food"), "15"),
            new XElement("armor", new XAttribute("type", "Hack"), new XAttribute("value", "0.3")));

        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        Assert.Equal(["15", "20"], updated.Elements("cost")
            .Where(element => (string?)element.Attribute("resourcetype") == "Food")
            .Select(element => element.Value));
        Assert.Equal("7", Assert.Single(updated.Elements("cost"),
            element => (string?)element.Attribute("resourcetype") == "CustomResource").Value);
        Assert.Equal(2, updated.Elements("armor").Count(element => (string?)element.Attribute("type") == "Hack"));
        Assert.Equal("0.5", (string?)Assert.Single(updated.Elements("armor"),
            element => (string?)element.Attribute("type") == "Divine").Attribute("value"));
    }

    [Fact]
    public void StatsPreserver_KeepsUnknownAttributesAndChildrenOnlyWhenManagedElementRemains()
    {
        var original = new XElement("unit",
            new XElement("spawn",
                new XAttribute("type", "dead"),
                new XAttribute("count", "1"),
                new XAttribute("futureAttribute", "kept"),
                new XElement("futureChild", "kept"),
                "LegacyUnit"),
            new XElement("initialshading",
                new XAttribute("type", "bronze"),
                new XAttribute("factor", "1"),
                new XAttribute("futureAttribute", "kept")));
        var updated = new XElement("unit",
            new XElement("spawn",
                new XAttribute("type", "dead"),
                new XAttribute("count", "2"),
                "LegacyUnit"));

        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        var spawn = Assert.Single(updated.Elements("spawn"));
        Assert.Equal("2", (string?)spawn.Attribute("count"));
        Assert.Equal("kept", (string?)spawn.Attribute("futureAttribute"));
        Assert.Equal("kept", spawn.Element("futureChild")?.Value);
        Assert.Null(updated.Element("initialshading"));
    }

    [Fact]
    public void StatsPreserver_KeepsUnknownResourceEntriesWhileOptionalSectionRemains()
    {
        var original = new XElement("unit",
            new XElement("carrycapacity", new XAttribute("resourcetype", "Food"), "10"),
            new XElement("carrycapacity", new XAttribute("resourcetype", "CustomResource"), "3"));
        var updated = new XElement("unit",
            new XElement("carrycapacity", new XAttribute("resourcetype", "Food"), "12"));

        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, updated);

        Assert.Equal("12", Assert.Single(updated.Elements("carrycapacity"),
            element => (string?)element.Attribute("resourcetype") == "Food").Value);
        Assert.Equal("3", Assert.Single(updated.Elements("carrycapacity"),
            element => (string?)element.Attribute("resourcetype") == "CustomResource").Value);
    }
}
