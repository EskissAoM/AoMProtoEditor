using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.EndToEnd)]
public sealed class CrossModuleReferenceWorkflowTests
{
    [Fact]
    public void Catalog_discovers_nested_xml_and_tactics_but_not_unrelated_files()
    {
        using var workspace = new TestWorkspace(nameof(Catalog_discovers_nested_xml_and_tactics_but_not_unrelated_files));
        var gameplay = workspace.CreateDirectory("game/data/gameplay");
        var tactics = workspace.CreateDirectory("game/data/tactics");
        File.WriteAllText(Path.Combine(gameplay, "custom_rules.xml"), "<rules />");
        File.WriteAllText(Path.Combine(tactics, "hero.tactics"), "<tactics />");
        File.WriteAllText(Path.Combine(gameplay, "notes.txt"), "not an XML reference source");

        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.Kind == TechnologyReferenceDocumentKind.Other);
        Assert.Contains(files, file => file.Kind == TechnologyReferenceDocumentKind.Tactics);
        Assert.DoesNotContain(files, file => file.Path.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(GameIdentifierKind.Ability, "<root><power name='Old'/><ability>Old</ability><castpower>Old</castpower><effect power='Old'/></root>", 3)]
    [InlineData(GameIdentifierKind.ProtoUnitCommand, "<root><protounitcommand><name>Old</name><sharedcommand>Old</sharedcommand></protounitcommand><unit><command>Old</command></unit><effect command='Old'/></root>", 3)]
    [InlineData(GameIdentifierKind.Tactics, "<root><unit><tactics>Old.tactics</tactics></unit><effect tactic='Old'/></root>", 2)]
    public void Shared_policy_renames_supported_identifiers_without_renaming_definitions(
        GameIdentifierKind kind,
        string xml,
        int expectedChanges)
    {
        var document = XDocument.Parse(xml);

        var changed = GameIdentifierReferencePolicy.RenameReferences(document, kind, "Old", "New.tactics");

        Assert.Equal(expectedChanges, changed);
        Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, "Old"));
        if (kind == GameIdentifierKind.Ability)
            Assert.Equal("Old", (string?)document.Descendants("power").Single().Attribute("name"));
        if (kind == GameIdentifierKind.ProtoUnitCommand)
            Assert.Equal("Old", document.Descendants("protounitcommand").Single().Element("name")?.Value);
        if (kind == GameIdentifierKind.Tactics)
            Assert.Equal("New", document.Descendants("tactics").Single().Value);
    }

    [Fact]
    public void TechType_policy_renames_exact_and_pipe_separated_references_without_substrings()
    {
        var document = XDocument.Parse(
            "<techtreemods><tech><techtype>Wolf</techtype><effect techtype='Wolf'><target type='TechType' excludetypes='Hero|Wolf|WolfHero'>Wolf</target></effect></tech></techtreemods>");

        var changed = GameIdentifierReferencePolicy.RenameReferences(
            document, GameIdentifierKind.TechType, "Wolf", "Canine");

        Assert.Equal(4, changed);
        Assert.Contains("WolfHero", document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.DoesNotContain(">Wolf<", document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.Equal("Hero|Canine|WolfHero", (string?)document.Descendants("target").Single().Attribute("excludetypes"));
    }

    [Fact]
    public void Plan_migrates_references_in_custom_nested_files_and_reports_each_occurrence()
    {
        using var workspace = new TestWorkspace(nameof(Plan_migrates_references_in_custom_nested_files_and_reports_each_occurrence));
        var firstPath = workspace.WriteXml("game/data/gameplay/custom.xml",
            "<root><unit><castpower>OldPower</castpower></unit></root>");
        var secondPath = workspace.WriteXml("game/data/tactics/custom.tactics",
            "<tactics><action power='OldPower'/></tactics>");
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);

        var plan = GameIdentifierReferenceMigrationPlan.Prepare(
            files,
            GameIdentifierKind.Ability,
            [new KeyValuePair<string, string>("OldPower", "NewPower")]);

        Assert.Equal(2, plan.Paths.Count);
        Assert.Equal(2, plan.AffectedReferences.Count);
        plan.Save((document, path) => document.Save(path, SaveOptions.DisableFormatting));
        Assert.Contains("NewPower", File.ReadAllText(firstPath), StringComparison.Ordinal);
        Assert.Contains("NewPower", File.ReadAllText(secondPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_blocks_unknown_reference_shapes_before_writing()
    {
        using var workspace = new TestWorkspace(nameof(Plan_blocks_unknown_reference_shapes_before_writing));
        var path = workspace.WriteXml("game/data/gameplay/custom.xml",
            "<root><futurepower>OldPower</futurepower></root>");
        var original = File.ReadAllBytes(path);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameIdentifierReferenceMigrationPlan.Prepare(
                ModReferenceFileCatalog.Discover(workspace.RootPath),
                GameIdentifierKind.Ability,
                [new KeyValuePair<string, string>("OldPower", "NewPower")]));

        Assert.Contains("unsupported reference", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

}
