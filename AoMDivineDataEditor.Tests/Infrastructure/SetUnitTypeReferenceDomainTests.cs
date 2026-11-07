using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class SetUnitTypeReferenceDomainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeOperandMigratesDespiteAnIndependentUnitWithTheSameName(bool directEffect)
    {
        using var workspace = new TestWorkspace(nameof(SetUnitTypeReferenceDomainTests));
        var effect = "<effect type='Data' subtype='SetUnitType' unittype='OldType' amount='1'><target type='ProtoUnit'>Concrete</target></effect>";
        var content = directEffect ? effect : "<effects>" + effect + "</effects>";
        var path = workspace.WriteXml("techtree_mods.xml", "<techtreemods><tech name='T'>" + content + "</tech></techtreemods>");
        var original = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var typeRename = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.UnitType,
            [new("OldType", "NewType")], oppositeUnitKindNames: ["OldType", "NewType"]);
        Assert.Single(typeRename.Paths);
        var changed = typeRename.GetDocument(path)!;
        Assert.Equal("NewType", (string?)changed.Descendants("effect").Single().Attribute("unittype"));
        Assert.Equal("Concrete", changed.Descendants("target").Single().Value);
        Assert.Single(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(XDocument.Load(path), GameIdentifierKind.UnitType, "OldType"));

        var unitRename = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit,
            [new("OldType", "RenamedConcreteUnit")], oppositeUnitKindNames: ["OldType", "RenamedConcreteUnit"]);
        Assert.Empty(unitRename.Paths);
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(XDocument.Load(path), GameIdentifierKind.ProtoUnit, "OldType"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void LookalikeExtensionStillRefusesAmbiguousReferences()
    {
        var document = XDocument.Parse("<root xmlns:x='urn:extension'><x:effect type='Data' subtype='SetUnitType' x:unittype='OldType'/></root>");
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, GameIdentifierKind.ProtoUnit, "OldType"));
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, GameIdentifierKind.UnitType, "OldType"));
    }
}
