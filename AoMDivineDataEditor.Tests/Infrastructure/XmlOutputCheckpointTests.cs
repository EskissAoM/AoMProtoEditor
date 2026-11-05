using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class XmlOutputCheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedMajorGodNestingUsesFinalParentIndentation(bool existingFile)
    {
        using var workspace = new TestWorkspace("god-output-indentation");
        var path = workspace.GetPath("major_gods_mods.xml");
        if (existingFile) File.WriteAllText(path, "<civsmods>\n  <civ><name>G</name></civ>\n</civsmods>");
        var god = XElement.Parse("<civ><name>G</name><titleid>T</titleid><icon>x</icon><future xml:space='preserve'>  <nested/> \n </future></civ>", LoadOptions.PreserveWhitespace);
        MajorGodCatalog.SetAgeTech(god, "ArchaicAge", "T");
        MajorGodCatalog.AddStartingUnit(MajorGodCatalog.AddStartingUnitsGroup(god), "Hoplite");
        MajorGodCatalog.AddStartingQueueUnit(MajorGodCatalog.AddStartingQueueGroup(god), "Hoplite");
        MajorGodCatalog.AddStartingResource(MajorGodCatalog.AddStartingResourcesGroup(god), "food", "100");
        var future = god.Element("future")!.ToString(SaveOptions.DisableFormatting);
        var document = new XDocument(new XElement("civsmods", god));
        PreservedXmlFile.SaveAtomic(document, path);
        var saved = XDocument.Load(path, LoadOptions.PreserveWhitespace).Root!.Element("civ")!;
        Assert.Equal(future, saved.Element("future")!.ToString(SaveOptions.DisableFormatting));
        foreach (var name in new[] { "agetech", "startingunits", "startingqueue", "startingresources" })
        {
            var group = saved.Element(name)!;
            Assert.Equal("\n    ", Assert.IsType<XText>(group.PreviousNode).Value);
            Assert.Equal("\n      ", Assert.IsType<XText>(group.Elements().First().PreviousNode).Value);
            Assert.Equal("\n    ", Assert.IsType<XText>(group.LastNode).Value);
        }
        var bytes = File.ReadAllBytes(path);
        PreservedXmlFile.SaveAtomic(XDocument.Load(path, LoadOptions.PreserveWhitespace), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlagsFollowUnitTypesEvenWithoutIdentityFields(bool identity)
    {
        var original = XElement.Parse("<unit name='U'><flag>A</flag><movementtype>land</movementtype>" +
            (identity ? "<animfile>x</animfile>" : "") + "<unittype>Unit</unittype><unittype>Other</unittype><future/></unit>");
        var edited = new XElement(original);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, edited);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(edited);
        var elements = edited.Elements().ToList();
        Assert.True(elements.IndexOf(edited.Element("flag")!) > elements.FindLastIndex(e => e.Name == "unittype"));
        Assert.Equal("land", edited.Element("movementtype")?.Value);
        Assert.NotNull(edited.Element("future"));
    }

    [Theory]
    [InlineData("unittype")]
    [InlineData("flag")]
    public void ChipAdditionKeepsExistingIdentityTriviaAndGroup(string tag)
    {
        var original = XElement.Parse($"<unit name='U'>\n  <displaynameid>ID</displaynameid>\n  <{tag} custom='keep'>Zulu<!--owned--></{tag}>\n  <tactics>x</tactics>\n  <future keep='yes'/>\n</unit>", LoadOptions.PreserveWhitespace);
        var edited = new XElement(original);
        if (tag == "unittype") ProtoXmlHandler.SetUnitTypeList(edited, ["Alpha", "Zulu"]);
        else ProtoXmlHandler.SetFlagList(edited, ["Alpha", "Zulu"]);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, edited);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(edited);
        Assert.Equal(new[] { "displaynameid", tag, tag, "tactics", "future" }, edited.Elements().Select(e => e.Name.LocalName));
        var zulu = Assert.Single(edited.Elements(tag), e => e.Value == "Zulu");
        Assert.Equal("keep", (string?)zulu.Attribute("custom"));
        Assert.Single(zulu.Nodes().OfType<XComment>());
        Assert.Empty(Assert.Single(edited.Elements(tag), e => e.Value == "Alpha").Nodes().OfType<XComment>());
        Assert.Equal("yes", (string?)edited.Element("future")?.Attribute("keep"));
        var once = edited.ToString(SaveOptions.DisableFormatting);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(new XElement(edited), edited);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(edited);
        Assert.Equal(once, edited.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LegacyUnitTypeBeforeIdentityIsRepairedWithoutDroppingUnknownXml()
    {
        var original = XElement.Parse("<unit name='U'><unittype>Unit</unittype><!--keep--><displaynameid>ID</displaynameid><animfile>x</animfile><future/><protoaction><name>A</name></protoaction></unit>");
        var edited = new XElement(original);
        ProtoXmlHandler.SetUnitTypeList(edited, ["AbstractArcher", "Unit"]);
        ProtoUnitStatsXmlPreserver.PreserveUnmanagedContent(original, edited);
        ProtoUnitXmlOrderPolicy.NormalizeForEditor(edited);
        Assert.Equal(new[] { "displaynameid", "animfile", "unittype", "unittype", "future", "protoaction" }, edited.Elements().Select(e => e.Name.LocalName));
        Assert.Single(edited.Nodes().OfType<XComment>());
        Assert.Equal("A", edited.Element("protoaction")?.Element("name")?.Value);
    }

    [Fact]
    public void MajorGodNestedIndentationIsRebasedAndMixedContentPreserved()
    {
        var god = XElement.Parse("<civ><name>G</name><titleid>T</titleid><icon>x</icon><future xml:space='preserve'>  <nested/> \n </future><mixed>text <child/> tail</mixed></civ>", LoadOptions.PreserveWhitespace);
        var future = god.Element("future")!.ToString(SaveOptions.DisableFormatting);
        var mixed = god.Element("mixed")!.ToString(SaveOptions.DisableFormatting);
        MajorGodCatalog.SetAgeTech(god, "ArchaicAge", "T");
        var resources = MajorGodCatalog.AddStartingResourcesGroup(god);
        MajorGodCatalog.AddStartingResource(resources, "food", "0");
        // Reparenting is the case where earlier child indentation becomes stale.
        var root = new XElement("civsmods", god);
        MajorGodCatalog.NormalizeDirectChildIndentation(root);
        var xml = root.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n");
        Assert.Contains("\n    <startingresources>\n      <food>0</food>\n    </startingresources>", xml);
        Assert.Contains("\n    <agetech age=\"ArchaicAge\">\n      <tech>T</tech>\n    </agetech>", xml);
        Assert.Equal(future, god.Element("future")!.ToString(SaveOptions.DisableFormatting));
        Assert.Equal(mixed, god.Element("mixed")!.ToString(SaveOptions.DisableFormatting));
        Assert.Contains("\n    <food>0</food>\n  </startingresources>", XmlSyntaxEditorService.FormatElementForPreview(god).Replace("\r\n", "\n"));
    }

    [Fact]
    public void AddedMajorGodIdentityPrecedesExistingStartingFields()
    {
        var god = XElement.Parse("<civ><name>G</name><startingresources><food>0</food></startingresources><future/></civ>");
        MajorGodCatalog.SetPass1Value(god, "titleid", "T");
        Assert.Equal(new[] { "name", "titleid", "startingresources", "future" }, god.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void OptionalTechnologyIconCanBeSetClearedAndSetAgain()
    {
        var tech = XElement.Parse("<tech name='T'><displaynameid>ID</displaynameid><status>ACTIVE</status><future/></tech>");
        var icon = new XElement("icon");
        TechnologyEditorView.SetOptionalIconValue(tech, icon, "  ");
        Assert.Null(tech.Element("icon"));
        TechnologyEditorView.SetOptionalIconValue(tech, icon, "icons/test");
        Assert.Equal(new[] { "displaynameid", "icon", "status", "future" }, tech.Elements().Select(e => e.Name.LocalName));
        TechnologyEditorView.SetOptionalIconValue(tech, icon, "");
        Assert.Null(tech.Element("icon"));
        TechnologyEditorView.SetOptionalIconValue(tech, icon, "icons/other");
        Assert.Equal("icons/other", tech.Element("icon")?.Value);
        Assert.NotNull(tech.Element("future"));
    }
}
