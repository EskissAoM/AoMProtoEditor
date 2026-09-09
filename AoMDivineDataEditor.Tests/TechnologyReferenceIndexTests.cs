using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyReferenceIndexTests
{
    [Fact]
    public void TechtreeIndexFindsEveryStructuredTechnologyReferenceWithoutTreatingTechTypesAsTechs()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="Owner">
                <techtype>ResearchableCommand</techtype>
                <prereqs><techstatus status="Active">OldTech</techstatus></prereqs>
                <effects>
                  <effect type="TechStatus" status="Active">OldTech</effect>
                  <effect type="RandomTech"><Tech>OldTech</Tech></effect>
                  <effect type="SetName" tech="OldTech" reqTech="OldTech" />
                  <effect type="Data" subtype="ResourceIfTechActive" tech="OldTech" />
                  <effect type="Data" subtype="ResearchPoints"><target type="Tech">OldTech</target></effect>
                </effects>
              </tech>
            </techtreemods>
            """);
        var source = new TechnologyReferenceSource(
            "Techtree", document, TechnologyReferenceDocumentKind.Techtree, CanMigrate: true);

        var usages = TechnologyReferenceIndex.Build([source], ["OldTech", "ResearchableCommand"]);

        Assert.Equal(7, usages.Count(usage => usage.TechnologyName == "OldTech"));
        Assert.DoesNotContain(usages, usage => usage.TechnologyName == "ResearchableCommand");
        Assert.All(usages, usage => Assert.True(usage.IsRecognized));
    }

    [Fact]
    public void ExternalIndexFindsKnownLeafFormsAndReportsUnknownTechFieldsWithoutRewritingThem()
    {
        var document = XDocument.Parse("""
            <root>
              <unit name="Owner"><tech>OldTech</tech><mysterytech>OldTech</mysterytech></unit>
            </root>
            """);
        var source = new TechnologyReferenceSource(
            "ProtoUnits", document, TechnologyReferenceDocumentKind.ProtoUnits, CanMigrate: true);

        var usages = TechnologyReferenceIndex.Build([source], ["OldTech"]);
        var changed = TechnologyReferenceIndex.RenameRecognizedReferences(source, "OldTech", "NewTech");

        Assert.Equal(2, usages.Count);
        Assert.Single(usages, usage => usage.IsRecognized);
        Assert.Single(usages, usage => !usage.IsRecognized);
        Assert.Equal(1, changed);
        Assert.Equal("NewTech", document.Descendants("tech").Single().Value);
        Assert.Equal("OldTech", document.Descendants("mysterytech").Single().Value);
    }

    [Fact]
    public void RenameChainsCollapseAndARevertRemovesThePendingMigration()
    {
        var first = TechnologyReferenceIndex.AddOrCollapseRename([], "A", "B");
        var second = TechnologyReferenceIndex.AddOrCollapseRename(first, "B", "C");
        var reverted = TechnologyReferenceIndex.AddOrCollapseRename(second, "C", "A");

        Assert.Equal(new TechnologyRenameOperation("A", "C"), Assert.Single(second));
        Assert.Empty(reverted);
    }

    [Fact]
    public void MigrationPlanIncludesOnlyFilesThatActuallyContainRecognizedReferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-tech-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var referencedPath = Path.Combine(directory, "proto_mods.xml");
            var unrelatedPath = Path.Combine(directory, "powers_mods.xml");
            File.WriteAllText(referencedPath, "<proto><unit name=\"U\"><tech>OldTech</tech></unit></proto>");
            File.WriteAllText(unrelatedPath, "<powers><power name=\"P\" /></powers>");

            var plan = TechnologyReferenceMigrationPlan.Prepare(
                [
                    new TechnologyReferenceFile(referencedPath, TechnologyReferenceDocumentKind.ProtoUnits, "ProtoUnits"),
                    new TechnologyReferenceFile(unrelatedPath, TechnologyReferenceDocumentKind.Powers, "Powers")
                ],
                [new TechnologyRenameOperation("OldTech", "NewTech")]);

            Assert.Equal(Path.GetFullPath(referencedPath), Assert.Single(plan.Paths));
            Assert.Single(plan.AffectedReferences);
            Assert.Empty(plan.UnknownReferences);
            Assert.Equal("NewTech", plan.GetDocument(referencedPath)!.Descendants("tech").Single().Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MigrationPlanReportsUnknownReferenceAndLeavesItUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-tech-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "proto_mods.xml");
            File.WriteAllText(path, "<proto><unit name=\"U\"><mysterytech>OldTech</mysterytech></unit></proto>");

            var plan = TechnologyReferenceMigrationPlan.Prepare(
                [new TechnologyReferenceFile(path, TechnologyReferenceDocumentKind.ProtoUnits, "ProtoUnits")],
                [new TechnologyRenameOperation("OldTech", "NewTech")]);

            Assert.Single(plan.UnknownReferences);
            Assert.Empty(plan.Paths);
            Assert.Null(plan.GetDocument(path));
            Assert.Contains("OldTech", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
