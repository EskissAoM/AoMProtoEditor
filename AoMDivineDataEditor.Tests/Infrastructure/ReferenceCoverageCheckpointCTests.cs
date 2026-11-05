using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceCoverageCheckpointCTests
{
    [Theory]
    [InlineData(GameIdentifierKind.ProtoUnit)]
    [InlineData(GameIdentifierKind.UnitType)]
    public void MixedPowerFieldsRefuseMissingCatalogOrEitherNameCollisionBeforeWriting(GameIdentifierKind kind)
    {
        string[] fields = ["<attracttype>Old</attracttype>", "<modelattachproto>Old</modelattachproto>",
            "<killunittype>Old</killunittype>", "<armor><abstracttype>Old</abstracttype></armor>"];
        foreach (var field in fields)
        foreach (var oppositeNames in new string[]?[] { null, ["oLD"], ["nEW"] })
        {
            using var workspace = new TestWorkspace("mixed-power-collision");
            var path = workspace.WriteXml("powers_mods.xml", $"<powersmod><power name='P'>{field}</power></powersmod>");
            var before = File.ReadAllBytes(path);
            var error = Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.Prepare(
                ModReferenceFileCatalog.Discover(workspace.RootPath), kind, [new("Old", "New")], oppositeUnitKindNames: oppositeNames));
            Assert.Contains("mixed Unit/Unit Type", error.Message);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData(GameIdentifierKind.Ability)]
    [InlineData(GameIdentifierKind.Culture)]
    [InlineData(GameIdentifierKind.MajorGod)]
    [InlineData(GameIdentifierKind.TechType)]
    [InlineData(GameIdentifierKind.ProtoUnitCommand)]
    [InlineData(GameIdentifierKind.Tactics)]
    public void AddedPowerReferencesDoNotBelongToUnrelatedKinds(GameIdentifierKind kind)
    {
        var xml = "<powersmod><power name='P'><shockwavevfx>Old</shockwavevfx><treeproto>Old</treeproto><treeeffectproto>Old</treeeffectproto><effectproto>Old</effectproto><vfxproto>Old</vfxproto><attracttype>Old</attracttype><modelattachproto>Old</modelattachproto><killunittype>Old</killunittype><armor><abstracttype>Old</abstracttype><vfxproto>Old</vfxproto></armor><swap swapto='Old'/><effects><effect type='AssignOnHitEffect' srcproto='Old'/></effects></power></powersmod>";
        var document = XDocument.Parse(xml);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, kind, "Old", "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, "Old"));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), document));
    }

    [Theory]
    [InlineData("<root><shockwavevfx>Old</shockwavevfx><swap swapto='Old'/></root>")]
    [InlineData("<powersmod><power><future><treeproto>Old</treeproto><attracttype>Old</attracttype></future></power></powersmod>")]
    [InlineData("<powersmod xmlns='urn:unknown'><power><vfxproto>Old</vfxproto></power></powersmod>")]
    [InlineData("<powersmod><power><effect type='Other' srcproto='Old'/></power></powersmod>")]
    public void UnestablishedContextsAreNeverAutomaticallyRewritten(string xml)
    {
        var document = XDocument.Parse(xml);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), document));
        // Conservative deletion continues to expose unknown exact-value dependencies.
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindPotentialUnrecognizedReferences(document, GameIdentifierKind.ProtoUnit, "Old"));
    }

    [Theory]
    [InlineData("<powersmod><power><effect type='TechActivate'><target type='Tech'>Old</target></effect></power></powersmod>", 1)]
    [InlineData("<powersmod><power><effect type='Other'><target type='Tech'>Old</target></effect></power></powersmod>", 0)]
    [InlineData("<powersmod><power><effect type='TechActivate'><target type='ProtoUnit'>Old</target></effect></power></powersmod>", 0)]
    [InlineData("<powersmod><power><future><effect type='TechActivate'><target type='Tech'>Old</target></effect></future></power></powersmod>", 0)]
    public void TechnologyPowerTargetRulesStayScoped(string xml, int expected)
    {
        var document = XDocument.Parse(xml);
        var source = new TechnologyReferenceSource("Powers", document, TechnologyReferenceDocumentKind.Powers);
        Assert.Equal(expected, TechnologyReferenceIndex.RenameRecognizedReferences(source, "Old", "New"));
        Assert.Equal(expected == 1 ? "New" : "Old", document.Descendants("target").Single().Value);
    }

    [Fact]
    public void ProtoUnitAndUnitTypeStillRecognizeTheirSharedTarget()
    {
        foreach (var kind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        {
            var document = XDocument.Parse("<techtreemods><tech name='T'><effects><effect><target type='ProtoUnit'>Old</target></effect></effects></tech></techtreemods>");
            Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, kind, "Old", "New"));
        }
    }
}
