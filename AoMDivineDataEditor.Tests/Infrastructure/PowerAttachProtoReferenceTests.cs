using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerAttachProtoReferenceTests
{
    [Theory]
    [InlineData("powers")]
    [InlineData("powersmod")]
    public void UnitRenameMigratesPowerAttachAndPreservesOtherContent(string root)
    {
        using var workspace = new TestWorkspace("power-attach-rename");
        var path = workspace.WriteXml("powers_mods.xml", $"<{root}><power name='P' type='TempUnit'><attachproto> agga </attachproto><future keep='yes'/></power></{root}>");
        var before = File.ReadAllBytes(path);
        var document = XDocument.Load(path);
        Assert.Single(GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.ProtoUnit, "agga"));
        Assert.True(Assert.Single(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, GameIdentifierKind.ProtoUnit, "agga")).IsRecognized);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.UnitType, "agga", "NewType"));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath),
            GameIdentifierKind.ProtoUnit, [new("agga", "RenamedAgga")]);
        Assert.Single(plan.Paths);
        Assert.Equal(before, File.ReadAllBytes(path));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((xml, target) => PreservedXmlFile.SaveAtomic(xml, target)));
        var saved = XDocument.Load(path);
        Assert.Equal("RenamedAgga", saved.Descendants("attachproto").Single().Value);
        Assert.Equal("P", (string?)saved.Descendants("power").Single().Attribute("name"));
        Assert.Equal("yes", (string?)saved.Descendants("future").Single().Attribute("keep"));
    }

    [Theory]
    [InlineData("<root><attachproto>agga</attachproto></root>")]
    [InlineData("<powersmod><power><future><attachproto>agga</attachproto></future></power></powersmod>")]
    [InlineData("<powersmod xmlns='urn:unknown'><power><attachproto>agga</attachproto></power></powersmod>")]
    public void UnknownAttachContextsStillRefuseWithoutWrites(string xml)
    {
        using var workspace = new TestWorkspace("power-attach-refusal");
        var path = workspace.WriteXml("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("agga", "RenamedAgga")]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
