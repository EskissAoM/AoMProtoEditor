using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests.Gods;

public sealed class GodModulesStructuralAndFieldValidationTests
{
    [Theory]
    [InlineData("<agetech age=\"ArchaicAge\"><tech>A</tech><Tech>B</Tech></agetech>")]
    [InlineData("<shades><protounit>A</protounit><chance>0.2</chance><Chance>0.3</Chance></shades>")]
    [InlineData("<startingqueue/><startingqueue mode=\"supremacy\"/>")]
    [InlineData("<startingresources><food>1</food><Food>2</Food></startingresources>")]
    [InlineData("<terraincreeps><terraincreep creep=\"A\" Creep=\"B\"/></terraincreeps>")]
    public void Major_god_safety_rejects_ambiguous_nested_structures(string nestedXml)
    {
        var document = XDocument.Parse($"<civsmods><civ><name>Test</name>{nestedXml}</civ></civsmods>");

        var validation = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.MajorGods);

        Assert.False(validation.IsSafeToEdit);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public void Major_god_safety_allows_repeating_collection_entries_and_unknown_xml()
    {
        var document = XDocument.Parse("""
            <civsmods>
              <!-- retained -->
              <civ>
                <name>Test</name>
                <startingunits><unit count="1" x="0" y="0" z="0">A</unit></startingunits>
                <startingunits mode="deathmatch"><unit count="1" x="0" y="0" z="0">B</unit></startingunits>
                <bonusunitspawning>
                  <damagegoal name="One"><reward>A</reward></damagegoal>
                  <resourcegoal name="Two"><reward>B</reward><unknown keep="yes"/></resourcegoal>
                </bonusunitspawning>
                <unmodeled><value>keep</value></unmodeled>
              </civ>
            </civsmods>
            """);

        var validation = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.MajorGods);

        Assert.True(validation.IsSafeToEdit);
    }

    [Fact]
    public void Minor_god_safety_rejects_case_variant_managed_attributes()
    {
        var document = XDocument.Parse(
            "<minorgodsmods><ClassicalAgeAthena portraitfilename=\"a.png\" PortraitFileName=\"b.png\"/></minorgodsmods>");

        var validation = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.MinorGods);

        Assert.False(validation.IsSafeToEdit);
        Assert.Contains(validation.Errors, issue => issue.Reason.Contains("portraitfilename", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Major_god_shared_fields_require_catalog_values_and_valid_presence_toggles()
    {
        var valid = XElement.Parse("""
            <civ>
              <displaynameid>STR_CIV_TEST</displaynameid>
              <rollovernameid>STR_CIV_TEST_LR</rollovernameid>
              <titleid>STR_CIV_TEST_T</titleid>
              <icon>resources/test/icon.dds</icon>
              <portrait>resources\test\portrait.png</portrait>
              <breakoutportrait>resources\test\breakout.png</breakoutportrait>
              <minimapborderimage>ui\minimap\border</minimapborderimage>
              <backgroundscenario>backgrounds\test.mythscn</backgroundscenario>
              <partisans>1</partisans>
              <devotioncommands/>
            </civ>
            """);

        GodModuleFieldValidation.ValidateMajorGodSharedFields(
            valid,
            ["resources\\test\\icon.png", "resources\\test\\portrait.png", "resources\\test\\breakout.png"],
            ["ui\\minimap\\border"],
            ["backgrounds\\test.mythscn"]);

        var invalid = new XElement(valid);
        invalid.Element("portrait")!.Value = "resources\\test\\missing.png";
        Assert.Throws<InvalidDataException>(() => GodModuleFieldValidation.ValidateMajorGodSharedFields(
            invalid,
            ["resources\\test\\icon.png", "resources\\test\\portrait.png", "resources\\test\\breakout.png"],
            ["ui\\minimap\\border"],
            ["backgrounds\\test.mythscn"]));
    }

    [Fact]
    public void Culture_save_validation_requires_stable_identity_and_display_string_id()
    {
        GodModuleFieldValidation.ValidateCultureDefinition(
            XElement.Parse("<culture><name>MyCulture</name><displaynameid>STR_CULTURE_MYCULTURE</displaynameid></culture>"),
            "MyCulture");

        Assert.Throws<InvalidDataException>(() => GodModuleFieldValidation.ValidateCultureDefinition(
            XElement.Parse("<culture><name>Bad Culture</name><displaynameid/></culture>"),
            "Original"));
    }

    [Fact]
    public void Minor_god_save_validation_requires_age_upgrade_technology_and_catalog_icons()
    {
        var valid = XElement.Parse(
            "<ClassicalAgeAthena portraitfilename=\"portraits/athena.dds\" portraiticonfilename=\"icons\\athena.png\"/>");

        GodModuleFieldValidation.ValidateMinorGodDefinition(
            valid,
            ["ClassicalAgeAthena"],
            ["portraits\\athena.png", "icons\\athena.png"]);

        Assert.Throws<InvalidDataException>(() => GodModuleFieldValidation.ValidateMinorGodDefinition(
            valid,
            ["HeroicAgeAthena"],
            ["portraits\\athena.png", "icons\\athena.png"]));
        Assert.Throws<InvalidDataException>(() => GodModuleFieldValidation.ValidateMinorGodDefinition(
            valid,
            ["ClassicalAgeAthena"],
            ["icons\\athena.png"]));
    }

    [Fact]
    public void Editor_save_routes_call_authoritative_god_module_validation()
    {
        var root = ProjectSource.RootPath;
        var majorGodEditor = File.ReadAllText(Path.Combine(root, "Windows", "MajorGodEditorView.axaml.cs"));
        var host = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var cultureEditor = File.ReadAllText(Path.Combine(root, "Windows", "CultureEditorWindow.cs"));

        Assert.Contains("ValidateCandidateStructure(candidate)", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("ValidateSharedFields(candidate)", majorGodEditor, StringComparison.Ordinal);
        Assert.Contains("GodModuleFieldValidation.ValidateCultureDefinition", host, StringComparison.Ordinal);
        Assert.Contains("GodModuleFieldValidation.ValidateMinorGodDefinition", host, StringComparison.Ordinal);
        Assert.Contains("Display name is required", cultureEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void Persistence_revalidates_nested_structure_after_mutation_and_before_writing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-god-structure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, MajorGodCatalog.ModFileName);
            const string original = "<civsmods><!-- keep --><civ><name>Test</name></civ></civsmods>";
            File.WriteAllText(path, original);
            var prepared = ModDocumentPersistence.Prepare(path, ModXmlDocumentKind.MajorGods);
            var god = prepared.Document.Root!.Element("civ")!;
            god.Add(new XElement("startingqueue"), new XElement("startingqueue", new XAttribute("mode", "supremacy")));
            var commitCalled = false;

            Assert.Throws<InvalidDataException>(() => ModDocumentPersistence.Execute(
                [prepared],
                null,
                null,
                () => commitCalled = true));

            Assert.False(commitCalled);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
