using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;
using Xunit.Abstractions;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", "ReferenceCoverageAudit")]
public sealed class ReferenceCoverageCheckpointEScopeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("empowerdata")]
    [InlineData("enemyempowerdata")]
    [InlineData("natureempowerdata")]
    public void DynamicUnitTypeNamesMustMigrateAndBlockDeletion(string section)
    {
        using var workspace = new TestWorkspace("coverage-e-dynamic");
        var xml = $"<protomods><unit name='Owner'><protoaction><name>Empower</name><{section}><Old rate='2'/><Other rate='3'/></{section}></protoaction></unit></protomods>";
        var path = workspace.WriteXml("proto_mods.xml", xml);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        Assert.True(ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.UnitType, "Old", workspace.RootPath).IsBlocked);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.UnitType, [new("Old", "New")], oppositeUnitKindNames: []);
        Assert.Single(plan.Paths);
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("<Old ", "<New ", StringComparison.Ordinal)), plan.GetDocument(path)));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), XDocument.Load(path)));
    }

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("revertothersto")]
    public void TransformDefinitionUnitReferencesMustBeSeenBySharedPlan(string field)
    {
        var definition = new ProtoUnitTransformDefinition { From = "Source", To = "Destination", Command = "C", Tech = "T", RevertOthersTo = "Revert" };
        if (field == "from") definition.From = "Old";
        if (field == "to") definition.To = "Old";
        if (field == "revertothersto") definition.RevertOthersTo = "Old";
        var document = new XDocument(new XElement("unittransforms", definition.ToElement()));
        using var workspace = new TestWorkspace("coverage-e-transform");
        var path = workspace.WriteXml("unit_transform_mods.xml", document.ToString());
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var deletion = ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.ProtoUnit, "Old", workspace.RootPath).IsBlocked;
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: []);
        output.WriteLine($"E|transform-{field}|renameWrites={plan.Paths.Count};deletionBlocked={deletion}");
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(deletion);
        Assert.Single(plan.Paths);
        var expected = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        expected.Descendants(field).Single().Value = "New";
        Assert.True(XNode.DeepEquals(expected, plan.GetDocument(path)));
    }

    [Theory]
    [InlineData("powers_mods.xml", "<powersmod><power name='P' type='Rain'><effect type='Damage' action='Old'><target type='ProtoUnit'>Owner</target></effect></power></powersmod>")]
    [InlineData("powers_mods.xml", "<powersmod><power name='P' type='Rain'><effect type='AssignOnHitEffect' srcproto='Owner' srcaction='Old'><target type='ProtoUnit'>Other</target></effect></power></powersmod>")]
    public void PowerActionReferenceMustFollowItsExplicitOwner(string file, string xml)
    {
        using var workspace = new TestWorkspace("coverage-e-action");
        var path = workspace.WriteXml(file, xml);
        var before = File.ReadAllBytes(path);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [],
            protoActionRenames: [new("Owner", "Old", "New")], oppositeUnitKindNames: []);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(plan.Paths);
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("Old", "New", StringComparison.Ordinal)), plan.GetDocument(path)));
    }

    [Fact]
    public void GrantedActionMustNotFollowTheTargetUnitsActionRename()
    {
        using var workspace = new TestWorkspace("coverage-e-source-action");
        var path = workspace.WriteXml("techtree_mods.xml", "<techtreemods><tech name='T'><effects><effect type='Data' subtype='ProtoActionAdd' unittype='Source' protoaction='Old'><target type='ProtoUnit'>Target</target></effect></effects></tech></techtreemods>");
        var before = File.ReadAllBytes(path);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [],
            protoActionRenames: [new("Target", "Old", "New")], oppositeUnitKindNames: []);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(plan.Paths);
    }

    [Theory]
    [InlineData("<root xmlns='urn:extension'><unit>Old</unit></root>")]
    [InlineData("<root xmlns='urn:extension'><effect proto='Old'/></root>")]
    public void UnknownNamespaceMustNotBeSilentlyRewritten(string xml)
    {
        using var workspace = new TestWorkspace("coverage-e-namespace");
        var path = workspace.WriteXml("extension.xml", xml);
        var before = File.ReadAllBytes(path);
        GameIdentifierReferenceMigrationPlan? plan = null;
        var error = Record.Exception(() => plan = GameIdentifierReferenceMigrationPlan.Prepare(
            ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: []));
        Assert.Equal(before, File.ReadAllBytes(path));
        if (error != null) Assert.IsType<InvalidOperationException>(error);
        else Assert.Empty(plan!.Paths);
    }

    [Fact]
    public void TacticsFileReferenceMustStillMigrateWithoutChangingInternalTactic()
    {
        using var workspace = new TestWorkspace("coverage-e-tactics-file");
        var path = workspace.WriteXml("proto_mods.xml", "<protomods><unit name='Owner'><tactics>Old.tactics</tactics></unit></protomods>");
        var tacticsPath = workspace.WriteXml("Old.tactics", "<tactics><tactic>Old<action>Attack</action></tactic></tactics>");
        var before = File.ReadAllBytes(tacticsPath);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.Tactics, [new("Old", "New")]);
        Assert.Single(plan.Paths);
        Assert.Equal("New", plan.GetDocument(path)!.Descendants("tactics").Single().Value);
        Assert.Equal(before, File.ReadAllBytes(tacticsPath));
    }
}
