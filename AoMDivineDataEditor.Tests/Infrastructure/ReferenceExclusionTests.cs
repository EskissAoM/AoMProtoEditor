using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceExclusionTests
{
    public static IEnumerable<object[]> PowerKinds()
    {
        foreach (var type in new[] { "UnitModify", "SingleModify", "NewMoon" })
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
            yield return [type, kind];
    }

    private static string Power(string type) => $"<powersmod><power name='P' type='{type}'><unitmodify apply='Add' amount='2' excludetypes='Other| Old |OldSuffix||OLD '>Damage</unitmodify><future keep='yes'/></power></powersmod>";

    [Theory, MemberData(nameof(PowerKinds))]
    public void PowerExclusionsRenameExactTokensAndBlockDeletion(string type, GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("exclusion-power");
        var xml = Power(type);
        var path = workspace.WriteXml("powers_mods.xml", xml);
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var usage = ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath);
        Assert.True(usage.IsBlocked);
        Assert.All(usage.References, reference => Assert.True(reference.IsRecognized));
        Assert.False(ModDeletionSafety.InspectIdentifier(files, kind, "Ol", workspace.RootPath).IsBlocked);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: []);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(plan.Paths);
        Assert.Single(plan.AffectedReferences);
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((document, target) => PreservedXmlFile.SaveAtomic(document, target)));
        var expected = XDocument.Parse(xml);
        expected.Descendants("unitmodify").Single().SetAttributeValue("excludetypes", "Other| New |OldSuffix||New ");
        Assert.True(XNode.DeepEquals(expected, XDocument.Load(path)));
        Assert.False(ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath).IsBlocked);
        Assert.True(ModDeletionSafety.InspectIdentifier(files, kind, "New", workspace.RootPath).IsBlocked);
    }

    [Theory, MemberData(nameof(PowerKinds))]
    public void MissingOrCollidingOppositeCatalogRefusesWithoutWrites(string type, GameIdentifierKind kind)
    {
        using var workspace = new TestWorkspace("exclusion-collision");
        var path = workspace.WriteXml("powers_mods.xml", Power(type));
        var before = File.ReadAllBytes(path);
        foreach (var opposite in new string[]?[] { null, ["oLD"], ["nEW"] })
        {
            Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
                ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: opposite));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("UnitModify")]
    [InlineData("SingleModify")]
    [InlineData("NewMoon")]
    public void TechTypeRenameAndDeletionDoNotClaimPowerExclusions(string type)
    {
        var document = XDocument.Parse(Power(type));
        var before = new XDocument(document);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.TechType, "Old", "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.TechType, "Old"));
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, GameIdentifierKind.TechType, "Old"));
        Assert.True(XNode.DeepEquals(before, document));
    }

    [Theory]
    [InlineData("TechType")]
    [InlineData("techAll")]
    [InlineData("")]
    public void TechnologyExclusionsRetainTheirDomainAndFormatting(string targetType)
    {
        using var workspace = new TestWorkspace("exclusion-technology");
        var typeAttribute = targetType.Length == 0 ? "" : $"type='{targetType}'";
        var xml = $"<techtreemods><tech name='T'><effects><effect type='Data' subtype='Cost'><target {typeAttribute} excludetypes='Other| Old |OldSuffix||OLD '/></effect></effects></tech></techtreemods>";
        var path = workspace.WriteXml("techtree_mods.xml", xml);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        foreach (var otherKind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            Assert.Empty(GameIdentifierReferenceMigrationPlan.Prepare(files, otherKind, [new("Old", "New")], oppositeUnitKindNames: []).Paths);
            Assert.False(ModDeletionSafety.InspectIdentifier(files, otherKind, "Old", workspace.RootPath).IsBlocked);
        }
        Assert.True(ModDeletionSafety.InspectIdentifier(files, GameIdentifierKind.TechType, "Old", workspace.RootPath).IsBlocked);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.TechType, [new("Old", "New")]);
        Assert.Single(plan.Paths);
        Assert.Equal("Other| New |OldSuffix||New ", plan.GetDocument(path)!.Descendants("target").Single().Attribute("excludetypes")!.Value);
        Assert.Single(plan.AffectedReferences);
    }

    [Theory]
    [InlineData("<powersmod><power type='Unknown'><unitmodify excludetypes='Other|Old'/></power></powersmod>")]
    [InlineData("<powersmod><power type='UnitModify'><future><unitmodify excludetypes='Other|Old'/></future></power></powersmod>")]
    [InlineData("<powersmod xmlns='urn:future'><power type='UnitModify'><unitmodify excludetypes='Other|Old'/></power></powersmod>")]
    [InlineData("<powersmod xmlns:x='urn:future'><power type='UnitModify'><unitmodify x:excludetypes='Other|Old'/></power></powersmod>")]
    [InlineData("<techtreemods><tech name='T'><effect><target type='ProtoUnit' excludetypes='Other|Old'/></effect></tech></techtreemods>")]
    [InlineData("<root><target type='techAll' excludetypes='Other|Old'/></root>")]
    public void UnknownExclusionContextRefusesAndRemainsPossibleDependency(string xml)
    {
        using var workspace = new TestWorkspace("exclusion-unknown");
        var path = workspace.WriteXml("custom.xml", xml);
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType, GameIdentifierKind.TechType })
        {
            Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: []));
            var inspection = ModDeletionSafety.InspectIdentifier(files, kind, "Old", workspace.RootPath);
            Assert.True(inspection.IsBlocked);
            Assert.All(inspection.References, reference => Assert.False(reference.IsRecognized));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }
}
