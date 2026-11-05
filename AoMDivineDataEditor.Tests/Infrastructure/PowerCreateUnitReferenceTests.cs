using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerCreateUnitReferenceTests
{
    [Theory]
    [InlineData("agga", "renamed")]
    [InlineData("Achilles| agga |AbeNoSeimei|AGGA|aggab", "Achilles| renamed |AbeNoSeimei|renamed|aggab")]
    public void RenameUpdatesExactSelectionTokensAndDeletionRecognizesThem(string input, string expected)
    {
        using var workspace = new TestWorkspace("power-create-rename");
        var path = workspace.WriteXml("powers_mods.xml", $"<powersmod><power name='P' type='TempUnit'><createunit quantity='2'>{input}</createunit></power></powersmod>");
        var original = File.ReadAllBytes(path);
        var document = XDocument.Load(path);
        Assert.Single(GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.ProtoUnit, "agga"));
        Assert.True(Assert.Single(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, GameIdentifierKind.ProtoUnit, "agga")).IsRecognized);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.UnitType, "agga", "wrong"));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("agga", "renamed")]);
        Assert.Single(plan.Paths);
        Assert.Equal(original, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates, () => plan.Save((xml, target) => PreservedXmlFile.SaveAtomic(xml, target)));
        var saved = XDocument.Load(path).Descendants("createunit").Single();
        Assert.Equal(expected, saved.Value);
        Assert.Equal("2", (string?)saved.Attribute("quantity"));
    }

    [Theory]
    [InlineData("<root><createunit>A|agga|B</createunit></root>")]
    [InlineData("<powersmod><power><future><createunit>A|agga|B</createunit></future></power></powersmod>")]
    public void UnknownListContextsBlockInsteadOfSilentlyMissingTokens(string xml)
    {
        using var workspace = new TestWorkspace("power-create-unknown");
        var path = workspace.WriteXml("custom.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("agga", "renamed")]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
