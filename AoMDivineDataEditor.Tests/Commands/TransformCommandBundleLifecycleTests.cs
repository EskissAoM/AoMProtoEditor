using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Commands;

public sealed class TransformCommandBundleLifecycleTests
{
    [Fact]
    public void OwnedIdentity_ToleratesStaleTargetButStillRequiresMatchingAssociatedTechnology()
    {
        Assert.False(TransformAssociatedTechnologyDefinition.IsOwnedConvention(
            "aggaToRhinoceros", "agga", "Arcus", "aggaToRhinoceros"));
        Assert.True(TransformAssociatedTechnologyDefinition.IsOwnedIdentity(
            "aggaToRhinoceros", "agga", "aggaToRhinoceros"));
        Assert.False(TransformAssociatedTechnologyDefinition.IsOwnedIdentity(
            "aggaToRhinoceros", "agga", "SomeOtherTechnology"));
    }

    [Fact]
    public void AssociatedTechnology_WritesOnlyInternalEconomyFieldsForNewDefinition()
    {
        var definition = TransformAssociatedTechnologyDefinition.Create("ArcusToArcusHero");
        definition.ResearchPoints = "12.5";
        definition.Costs["Food"] = "50";
        definition.Costs["Wood"] = "10";

        var element = definition.ToElement();

        Assert.Equal("ArcusToArcusHero", (string?)element.Attribute("name"));
        Assert.Equal("12.5", element.Element("researchpoints")?.Value);
        Assert.Equal("50", element.Elements("cost").Single(cost => (string?)cost.Attribute("resourcetype") == "Food").Value);
        Assert.Equal(2, element.Elements("cost").Count());
        Assert.DoesNotContain(element.Elements("cost"), cost =>
            (string?)cost.Attribute("resourcetype") is "Gold" or "Favor");
        Assert.Equal("UNOBTAINABLE", element.Element("status")?.Value);
        Assert.Contains(element.Elements("flag"), flag => flag.Value == "HideAllNotifications");
        Assert.Null(element.Element("displaynameid"));
        Assert.Null(element.Element("rollovertextid"));
        Assert.Null(element.Element("icon"));
    }

    [Fact]
    public void AssociatedTechnology_RemovesEmptyAndZeroCostsFromExistingDefinition()
    {
        var definition = TransformAssociatedTechnologyDefinition.FromElement(XElement.Parse("""
            <tech name="ArcusToHero">
              <cost resourcetype="Food">0.000</cost>
              <cost resourcetype="Wood">25</cost>
              <cost resourcetype="Gold">0</cost>
              <cost resourcetype="Favor"></cost>
              <researchpoints>7</researchpoints>
            </tech>
            """));

        var element = definition.ToElement();

        var cost = Assert.Single(element.Elements("cost"));
        Assert.Equal("Wood", (string?)cost.Attribute("resourcetype"));
        Assert.Equal("25", cost.Value);
    }

    [Fact]
    public void Duplicate_CreatesUnitOwnedCommandTransformAndTechnology()
    {
        var sourceUnit = XElement.Parse("""
            <unit name="Arcus">
              <transformcommand>ArcusToArcusHero</transformcommand>
              <command row="0" column="1">ArcusToArcusHero</command>
            </unit>
            """);
        var duplicate = new XElement(sourceUnit);
        duplicate.SetAttributeValue("name", "ArcusCopy");
        var commands = XDocument.Parse("""
            <protounitcommandmods><protounitcommand><name>ArcusToArcusHero</name><associatedtech>ArcusToArcusHero</associatedtech><transformselected></transformselected></protounitcommand></protounitcommandmods>
            """);
        var transforms = XDocument.Parse("""
            <unittransformmods><transform><from>Arcus</from><to>ArcusHero</to><tech>HeroicAgeGreek</tech><command>ArcusToArcusHero</command></transform></unittransformmods>
            """);
        var technologies = XDocument.Parse("""
            <techtreemods><tech name="ArcusToArcusHero"><cost resourcetype="Food">25</cost><researchpoints>8</researchpoints></tech></techtreemods>
            """);

        var created = TransformCommandBundleLifecycle.Duplicate(
            sourceUnit, duplicate, "ArcusCopy", commands, transforms, technologies);

        Assert.Single(created);
        Assert.Equal("ArcusCopyToArcusHero", duplicate.Element("transformcommand")?.Value);
        Assert.Equal("ArcusCopyToArcusHero", duplicate.Element("command")?.Value);
        Assert.Contains(commands.Descendants("name"), element => element.Value == "ArcusCopyToArcusHero");
        Assert.Contains(transforms.Descendants("transform"), element =>
            element.Element("from")?.Value == "ArcusCopy" &&
            element.Element("command")?.Value == "ArcusCopyToArcusHero");
        var copiedTech = technologies.Descendants("tech").Single(element =>
            (string?)element.Attribute("name") == "ArcusCopyToArcusHero");
        Assert.Equal("8", copiedTech.Element("researchpoints")?.Value);
        Assert.Equal("25", copiedTech.Elements("cost").Single(cost =>
            (string?)cost.Attribute("resourcetype") == "Food").Value);
    }

    [Fact]
    public void Duplicate_CanReadBuiltInSourcesAndWriteOnlyTheRenamedBundleToModDocuments()
    {
        var sourceUnit = XElement.Parse("""
            <unit name="Calpulli">
              <command row="1" column="3">CalpulliToLivestockPen</command>
            </unit>
            """);
        var duplicate = new XElement(sourceUnit);
        duplicate.SetAttributeValue("name", "CalpulliDuplicate");
        var sourceCommands = XDocument.Parse("""
            <protounitcommands><protounitcommand><name>CalpulliToLivestockPen</name><associatedtech>CalpulliToLivestockPen</associatedtech><transform /></protounitcommand></protounitcommands>
            """);
        var sourceTransforms = XDocument.Parse("""
            <unittransforms><transform><from>Calpulli</from><to>CalpulliLivestockPen</to><command>CalpulliToLivestockPen</command></transform></unittransforms>
            """);
        var sourceTechnologies = XDocument.Parse("""
            <techtree><tech name="CalpulliToLivestockPen"><cost resourcetype="Wood">150</cost><cost resourcetype="Favor">0</cost><researchpoints>10</researchpoints></tech></techtree>
            """);
        var modCommands = XDocument.Parse("<protounitcommandmods />");
        var modTransforms = XDocument.Parse("<unittransformmods />");
        var modTechnologies = XDocument.Parse("<techtreemods />");

        var created = TransformCommandBundleLifecycle.Duplicate(
            sourceUnit,
            duplicate,
            "CalpulliDuplicate",
            sourceCommands,
            sourceTransforms,
            sourceTechnologies,
            modCommands,
            modTransforms,
            modTechnologies);

        Assert.Single(created);
        Assert.Equal("CalpulliDuplicateToCalpulliLivestockPen", duplicate.Element("command")?.Value);
        Assert.Equal("CalpulliDuplicateToCalpulliLivestockPen", modCommands.Descendants("name").Single().Value);
        Assert.Equal("CalpulliDuplicate", modTransforms.Descendants("from").Single().Value);
        Assert.Equal("CalpulliDuplicateToCalpulliLivestockPen", modTransforms.Descendants("command").Single().Value);
        var technology = modTechnologies.Descendants("tech").Single();
        Assert.Equal("CalpulliDuplicateToCalpulliLivestockPen", (string?)technology.Attribute("name"));
        Assert.Equal("150", technology.Elements("cost").Single().Value);
        Assert.DoesNotContain(technology.Elements("cost"), cost =>
            (string?)cost.Attribute("resourcetype") == "Favor");
        Assert.Equal("CalpulliToLivestockPen", sourceCommands.Descendants("name").Single().Value);
    }

    [Fact]
    public void RenameUnit_RenamesOwnedBundleAndAssignments()
    {
        var unit = XElement.Parse("""
            <unit name="NewArcus"><transformcommand>ArcusToArcusHero</transformcommand><command>ArcusToArcusHero</command></unit>
            """);
        var commands = XDocument.Parse("""
            <protounitcommandmods><protounitcommand><name>ArcusToArcusHero</name><associatedtech>ArcusToArcusHero</associatedtech></protounitcommand></protounitcommandmods>
            """);
        var transforms = XDocument.Parse("""
            <unittransformmods><transform><from>Arcus</from><to>ArcusHero</to><command>ArcusToArcusHero</command></transform></unittransformmods>
            """);
        var technologies = XDocument.Parse("""
            <techtreemods><tech name="ArcusToArcusHero"><researchpoints>5</researchpoints></tech></techtreemods>
            """);

        var renamed = TransformCommandBundleLifecycle.RenameUnit(
            unit, "Arcus", "NewArcus", commands, transforms, technologies);

        Assert.Single(renamed);
        Assert.Equal("NewArcusToArcusHero", unit.Element("transformcommand")?.Value);
        Assert.Equal("NewArcusToArcusHero", commands.Descendants("name").Single().Value);
        Assert.Equal("NewArcusToArcusHero", commands.Descendants("associatedtech").Single().Value);
        Assert.Equal("NewArcus", transforms.Descendants("from").Single().Value);
        Assert.Equal("NewArcusToArcusHero", transforms.Descendants("command").Single().Value);
        Assert.Equal("NewArcusToArcusHero", (string?)technologies.Descendants("tech").Single().Attribute("name"));
    }

    [Fact]
    public void RenameTarget_RenamesTheCommandTransformAndAssociatedTechnologyTogether()
    {
        var commands = XDocument.Parse("""
            <protounitcommandmods><protounitcommand><name>ArcusToArcusHero</name><associatedtech>ArcusToArcusHero</associatedtech><unitcommand /></protounitcommand></protounitcommandmods>
            """);
        var transforms = XDocument.Parse("""
            <unittransformmods><transform><from>Arcus</from><to>ArcusHero</to><tech>HeroicAgeGreek</tech><command>ArcusToArcusHero</command></transform></unittransformmods>
            """);
        var technologies = XDocument.Parse("""
            <techtreemods><tech name="ArcusToArcusHero"><cost resourcetype="Food">25</cost><researchpoints>8</researchpoints></tech></techtreemods>
            """);

        var renamed = TransformCommandBundleLifecycle.RenameTarget(
            "ArcusToArcusHero", "Priest", commands, transforms, technologies);

        Assert.Equal("ArcusToPriest", renamed.New.CommandName);
        Assert.Equal("ArcusToPriest", commands.Descendants("name").Single().Value);
        Assert.Equal("ArcusToPriest", commands.Descendants("associatedtech").Single().Value);
        Assert.Equal("Priest", transforms.Descendants("to").Single().Value);
        Assert.Equal("ArcusToPriest", transforms.Descendants("command").Single().Value);
        Assert.Equal("ArcusToPriest", (string?)technologies.Descendants("tech").Single().Attribute("name"));
        Assert.Equal("25", technologies.Descendants("cost").Single(element =>
            (string?)element.Attribute("resourcetype") == "Food").Value);
        Assert.Equal("8", technologies.Descendants("researchpoints").Single().Value);
        Assert.DoesNotContain(commands.Descendants("name"), element => element.Value == "ArcusToArcusHero");
        Assert.DoesNotContain(transforms.Descendants("command"), element => element.Value == "ArcusToArcusHero");
    }

    [Fact]
    public void RenameTarget_RepairsBundleWhenSavedTargetIsAlreadyOutOfSync()
    {
        var commands = XDocument.Parse("""
            <protounitcommandmods><protounitcommand><name>aggaToRhinoceros</name><associatedtech>aggaToRhinoceros</associatedtech><unitcommand /></protounitcommand></protounitcommandmods>
            """);
        var transforms = XDocument.Parse("""
            <unittransformmods><transform><from>agga</from><to>Arcus</to><command>aggaToRhinoceros</command></transform></unittransformmods>
            """);
        var technologies = XDocument.Parse("""
            <techtreemods><tech name="aggaToRhinoceros"><cost resourcetype="Food">25</cost><researchpoints>10</researchpoints></tech></techtreemods>
            """);

        var renamed = TransformCommandBundleLifecycle.RenameTarget(
            "aggaToRhinoceros", "Priest", commands, transforms, technologies);

        Assert.Equal("aggaToPriest", renamed.New.CommandName);
        Assert.Equal("aggaToPriest", commands.Descendants("name").Single().Value);
        Assert.Equal("aggaToPriest", commands.Descendants("associatedtech").Single().Value);
        Assert.Equal("Priest", transforms.Descendants("to").Single().Value);
        Assert.Equal("aggaToPriest", transforms.Descendants("command").Single().Value);
        Assert.Equal("aggaToPriest", (string?)technologies.Descendants("tech").Single().Attribute("name"));
        Assert.DoesNotContain("aggaToRhinoceros", commands.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("aggaToRhinoceros", transforms.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("aggaToRhinoceros", technologies.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RenameTarget_SavesAllThreeBundleFilesWithStructuralIndentation()
    {
        using var workspace = new TestWorkspace(nameof(RenameTarget_SavesAllThreeBundleFilesWithStructuralIndentation));
        var commandPath = workspace.WriteText("proto_unit_command_mods.xml", """
            <protounitcommandmods>
              <protounitcommand>
                <name>ArcusToArcusHero</name>
                <associatedtech>ArcusToArcusHero</associatedtech>
                <unitcommand></unitcommand>
              </protounitcommand>
            </protounitcommandmods>
            """);
        var transformPath = workspace.WriteText("unit_transform_mods.xml", """
            <unittransformmods>
              <transform>
                <from>Arcus</from>
                <to>ArcusHero</to>
                <tech>HeroicAgeGreek</tech>
                <command>ArcusToArcusHero</command>
              </transform>
            </unittransformmods>
            """);
        var technologyPath = workspace.WriteText("techtree_mods.xml", """
            <techtreemods>
              <tech name="ArcusToArcusHero">
                <cost resourcetype="Food">25</cost>
                <researchpoints>8</researchpoints>
              </tech>
            </techtreemods>
            """);
        var commands = ModDocumentPersistence.Prepare(commandPath, ModXmlDocumentKind.ProtoUnitCommands);
        var transforms = ModDocumentPersistence.Prepare(transformPath, ModXmlDocumentKind.UnitTransforms);
        var technologies = ModDocumentPersistence.Prepare(technologyPath, ModXmlDocumentKind.Technologies);

        TransformCommandBundleLifecycle.RenameTarget(
            "ArcusToArcusHero", "Priest", commands.Document, transforms.Document, technologies.Document);
        PreservedXmlFile.SaveAtomic(
            commands.Document, commandPath, ProtoUnitCommandDefinition.ExpandEmptyFlagElements);
        PreservedXmlFile.SaveAtomic(transforms.Document, transformPath);
        PreservedXmlFile.SaveAtomic(technologies.Document, technologyPath);

        var commandXml = File.ReadAllText(commandPath);
        var transformXml = File.ReadAllText(transformPath);
        var technologyXml = File.ReadAllText(technologyPath);
        Assert.Contains("\n  <protounitcommand>\n    <name>ArcusToPriest</name>\n", commandXml, StringComparison.Ordinal);
        Assert.Contains("\n  <transform>\n    <from>Arcus</from>\n    <to>Priest</to>\n", transformXml, StringComparison.Ordinal);
        Assert.Contains("\n  <tech name=\"ArcusToPriest\">\n", technologyXml, StringComparison.Ordinal);
        Assert.Contains("\n    <researchpoints>8</researchpoints>\n", technologyXml, StringComparison.Ordinal);
        Assert.DoesNotContain("ArcusToArcusHero", commandXml, StringComparison.Ordinal);
        Assert.DoesNotContain("ArcusToArcusHero", transformXml, StringComparison.Ordinal);
        Assert.DoesNotContain("ArcusToArcusHero", technologyXml, StringComparison.Ordinal);
    }
}
