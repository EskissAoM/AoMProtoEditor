using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class MajorGodXmlOrderPolicyTests
{
    [Fact]
    public void AllSpecialMechanicsFollowStartingFieldsInTheSharedFormSequence()
    {
        string[] expected = ["name", "statsid", "displaynameid", "titleid", "rollovernameid", "icon", "portrait",
            "breakoutportrait", "culture", "key", "minimapborderimage", "backgroundscenario", "agetech",
            "deathmatchtech", "teamtech", .. MajorGodCatalog.EconomicFieldOrder,
            "startingunits", "startingqueue", "startingresources", "partisans", "devotioncommands",
            .. MajorGodCatalog.SpecialMechanicDefinitions.Select(definition => definition.Tag)];
        var god = new XElement("civ", expected.Reverse().Select(tag => new XElement(tag)));
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(expected, god.Elements().Select(e => e.Name.LocalName));
        var once = god.ToString(SaveOptions.DisableFormatting);
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(once, god.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void FieldCommentsMoveWithTheirFieldAndOpaqueBoundariesAreNotCrossed()
    {
        var god = XElement.Parse("<civ><icon>x</icon><!--culture note--><culture>Greek</culture><name>A</name><future/><statsid>SP</statsid></civ>");
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(new[] { "name", "icon", "culture", "future", "statsid" }, god.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("culture note", Assert.IsType<XComment>(god.Element("culture")!.PreviousNode).Value);
    }

    [Theory]
    [InlineData("<civ xml:space='preserve'><icon>x</icon><name>A</name></civ>")]
    [InlineData("<civ>prefix<icon>x</icon><name>A</name></civ>")]
    [InlineData("<civ><![CDATA[ ]]><icon>x</icon><name>A</name></civ>")]
    public void ProtectedGodContentIsNotSortedOrReformatted(string xml)
    {
        var god = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var before = god.ToString(SaveOptions.DisableFormatting);
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(before, god.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void StartingGroupsAndTheirUnitSequencesRemainStable()
    {
        var god = XElement.Parse("<civ><startingunits mode='deathmatch'><unit>B</unit><unit>A</unit></startingunits><icon>x</icon><startingunits><unit>C</unit></startingunits><name>G</name></civ>");
        var groups = god.Elements("startingunits").ToArray();
        var shapes = groups.Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray();
        MajorGodCatalog.NormalizeEditorFieldOrder(god);
        Assert.Equal(groups, god.Elements("startingunits"));
        Assert.Equal(shapes, god.Elements("startingunits").Select(e => e.ToString(SaveOptions.DisableFormatting)));
    }

    [Fact]
    public void NewRootFieldsUseTheApprovedRankWithoutStealingLeadingComments()
    {
        var god = XElement.Parse("<civ><name>A</name><!--culture note--><culture>Greek</culture><startingresources/></civ>");
        MajorGodCatalog.SetPass1Value(god, "icon", "x");
        MajorGodCatalog.SetAssociatedTech(god, "teamtech", "T");
        MajorGodCatalog.SetEconomicValue(god, "buildingefficiency", "1");
        MajorGodCatalog.AddStartingQueueGroup(god);
        MajorGodCatalog.AddSpecialMechanic(god, "shades");
        Assert.Equal(new[] { "name", "icon", "culture", "teamtech", "buildingefficiency", "startingqueue", "startingresources", "shades" }, god.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("culture", god.Nodes().OfType<XComment>().Single().NodesAfterSelf().OfType<XElement>().First().Name.LocalName);
    }
}
