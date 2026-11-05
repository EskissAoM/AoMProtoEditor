using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceIdentityContextTests
{
    [Theory]
    [InlineData("DeadTransform")]
    [InlineData("PlacementRulesOverride")]
    [InlineData("TimeShiftingAdd")]
    [InlineData("PartisanUnit")]
    [InlineData("ProtoActionAdd")]
    [InlineData("ActionAddAttachingUnit")]
    [InlineData("Projectile")]
    [InlineData("SharedBuildLimitUnit")]
    public void UnitOnlyAttributesPreserveTheIndependentUnitType(string subtype)
    {
        var xml = Tech($"<effect type='Data' subtype='{subtype}' unittype='Old'><target type='ProtoUnit'>Other</target></effect>");
        AssertUnitOnly(xml);
    }

    [Theory]
    [InlineData("<techtreemods><tech name='T'><effects><effect type='CreateUnit' unit='Old'/></effects></tech></techtreemods>")]
    [InlineData("<techtreemods><tech name='T'><effects><effect type='Data' subtype='Enable'><target type='ProtoUnit'>Old</target></effect></effects></tech></techtreemods>")]
    [InlineData("<civs><civ><name>C</name><buildingchain><chainablebuilding>Old</chainablebuilding></buildingchain></civ></civs>")]
    public void UnitOnlyFieldsKeepPositiveMigration(string xml) => AssertUnitOnly(xml);

    private static void AssertUnitOnly(string xml)
    {
        using var workspace = new TestWorkspace("identity-unit-only");
        var path = workspace.WriteXml("context.xml", xml);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        var before = File.ReadAllBytes(path);
        Assert.Empty(GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.UnitType, [new("Old", "New")], oppositeUnitKindNames: ["Old", "New"]).Paths);
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(XDocument.Parse(xml), GameIdentifierKind.UnitType, "Old"));
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, GameIdentifierKind.ProtoUnit, [new("Old", "New")], oppositeUnitKindNames: ["Old", "New"]);
        Assert.Single(plan.Paths); // Same-name opposite definitions do not prohibit a Unit-only reference.
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("Old", "New")), plan.GetDocument(path)));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("WorkRate")]
    [InlineData("MinWorkRate")]
    public void ResourceModeUsesExistingResourceCatalogAndGuardsDestination(string subtype)
    {
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        foreach (var resource in ProtoConstants.KnownResourceTypes)
        {
            var doc = XDocument.Parse(Tech($"<effect type='Data' subtype='{subtype}' unittype='{resource}'/>"));
            Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, kind, resource, "New"));
            Assert.Empty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(doc, kind, resource));
            Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(doc, kind, resource));
            using var workspace = new TestWorkspace("identity-resource-destination");
            var path = workspace.WriteXml("techtree_mods.xml", Tech($"<effect type='Data' subtype='{subtype}' unittype='Old'/>"));
            var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", resource)], oppositeUnitKindNames: []));
            Assert.Single(GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: []).Paths);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("<techtreemods><tech name='T'><effects><effect type='Data' subtype='Damage'><target type='ProtoUnit'>Old</target></effect></effects></tech></techtreemods>")]
    [InlineData("<powersmod><power name='P' type='Rain'><effect type='Damage'><target type='ProtoUnit'>Old</target></effect></power></powersmod>")]
    [InlineData("<civs><civ><name>C</name><buildingchain><anchor>Old</anchor></buildingchain></civ></civs>")]
    public void MixedReferencesRequireCatalogAndRejectBothCollisions(string xml)
    {
        using var workspace = new TestWorkspace("identity-mixed");
        var path = workspace.WriteXml("context.xml", xml);
        var before = File.ReadAllBytes(path);
        var files = ModReferenceFileCatalog.Discover(workspace.RootPath);
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            foreach (string[]? names in new string[]?[] { null, [" old "], ["NEW"] })
                Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: names));
            var plan = GameIdentifierReferenceMigrationPlan.Prepare(files, kind, [new("Old", "New")], oppositeUnitKindNames: []);
            Assert.Single(plan.Paths);
            Assert.True(XNode.DeepEquals(XDocument.Parse(xml.Replace("Old", "New")), plan.GetDocument(path)));
            Assert.NotEmpty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(XDocument.Parse(xml), kind, "Old"));
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("<root xmlns:x='urn:extension'><x:unit>Old</x:unit></root>")]
    [InlineData("<root xmlns:x='urn:extension'><effect x:proto='Old'/></root>")]
    [InlineData("<root xmlns='urn:extension'><unit xmlns=''>Old</unit></root>")]
    public void ExtensionsArePreservedAndRefused(string xml)
    {
        var doc = XDocument.Parse(xml);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), doc));
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(doc, GameIdentifierKind.ProtoUnit, "Old"));
        Assert.All(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(doc, GameIdentifierKind.ProtoUnit, "Old"), location => Assert.False(location.IsRecognized));
    }

    [Fact]
    public void DynamicExtensionNamesAreNotRewritten()
    {
        var doc = XDocument.Parse("<root xmlns:x='urn:extension'><protoaction><empowerdata><x:Old/></empowerdata></protoaction></root>");
        var before = new XDocument(doc);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.UnitType, "Old", "New"));
        Assert.True(XNode.DeepEquals(before, doc));
    }

    [Fact]
    public void TacticFileRenamePreservesInternalNamesInTheSameMod()
    {
        using var workspace = new TestWorkspace("identity-tactics");
        var unit = workspace.WriteXml("proto_mods.xml", "<protomods><unit name='U'><tactics>Old.tactics</tactics></unit></protomods>");
        var tech = workspace.WriteXml("techtree_mods.xml", Tech("<effect type='Data' subtype='TacticEnable' tactic='Old'/ >".Replace("/ >", "/>")));
        var power = workspace.WriteXml("powers_mods.xml", "<powersmod><power name='P' type='Rain'><effect type='TacticEnable' tactic='Old'/></power></powersmod>");
        var tactics = workspace.WriteXml("Old.tactics", "<tactics><tactic>Old<action>Attack</action></tactic></tactics>");
        var others = new[] { tech, power, tactics }.ToDictionary(path => path, File.ReadAllBytes);
        var plan = GameIdentifierReferenceMigrationPlan.Prepare(ModReferenceFileCatalog.Discover(workspace.RootPath), GameIdentifierKind.Tactics, [new("Old", "New")]);
        Assert.Equal(unit, Assert.Single(plan.Paths));
        FileIntegrityTransaction.Execute(plan.Paths, plan.ExpectedStates,
            () => plan.Save((document, path) => PreservedXmlFile.SaveAtomic(document, path)));
        Assert.Equal("New", XDocument.Load(unit).Descendants("tactics").Single().Value);
        foreach (var (path, bytes) in others) Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static string Tech(string effect) => $"<techtreemods><tech name='T'><effects>{effect}</effects></tech></techtreemods>";
}
