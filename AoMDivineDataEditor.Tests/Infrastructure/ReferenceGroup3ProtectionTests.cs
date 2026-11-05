using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ReferenceGroup3ProtectionTests
{
    public static IEnumerable<object[]> Fields() => ReferenceCoverageCheckpointETests.References()
        .Where(row => IsGroup3((string)row[0]));

    private static bool IsGroup3(string id) =>
        new[] { "RC03", "RC11", "RC12", "RC13", "RC14", "RC17", "RC24", "RC25", "RC27", "RC29" }
            .Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal)) && !id.Contains("excludetypes");

    public static IEnumerable<object[]> MixedFields() => Fields().Where(row =>
        (string)row[0] == "RC29-generator" || (GameIdentifierKind)row[1] == GameIdentifierKind.ProtoUnit &&
        (((string)row[0]).StartsWith("RC13", StringComparison.Ordinal) ||
         ((string)row[0]).StartsWith("RC17", StringComparison.Ordinal) ||
         (string)row[0] is "RC27-restrictempowertype" or "RC27-modifyabstracttype" or "RC27-modifyunittype" or "RC27-modifydamagetargettype"));

    [Theory, MemberData(nameof(Fields))]
    public void ExtensionVersionsOfEveryFieldRemainUntouched(string id, GameIdentifierKind kind, string file, string xml)
    {
        var doc = XDocument.Parse(xml);
        doc.Root!.Name = XName.Get(doc.Root.Name.LocalName, "urn:extension");
        var before = new XDocument(doc);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, kind, "Old", "New"));
        Assert.True(XNode.DeepEquals(before, doc), id + " " + file);
    }

    [Theory, MemberData(nameof(MixedFields))]
    public void MixedFieldsCannotBypassIdentityGuard(string id, GameIdentifierKind kind, string file, string xml)
    {
        var doc = XDocument.Parse(xml);
        Assert.Contains(kind, new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType });
        foreach (var renamedKind in new[] { GameIdentifierKind.ProtoUnit, GameIdentifierKind.UnitType })
        foreach (string[]? names in new string[]?[] { null, ["Old"], ["New"] })
            Assert.Throws<InvalidOperationException>(() => GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(
                doc, renamedKind, "Old", "New", names, file));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), doc), id);
    }

    [Theory]
    [InlineData("<powersmod><power type='UnitModify'><future><birthvfx>Old</birthvfx></future></power></powersmod>", GameIdentifierKind.ProtoUnit)]
    [InlineData("<powersmod><power type='Unknown'><birthvfx>Old</birthvfx></power></powersmod>", GameIdentifierKind.ProtoUnit)]
    [InlineData("<powersmod><power type='SwapUnit'><future><abstracttype swapto='Old'>Other</abstracttype></future></power></powersmod>", GameIdentifierKind.ProtoUnit)]
    [InlineData("<protomods><unit name='U'><future><modifyciv>Old</modifyciv></future></unit></protomods>", GameIdentifierKind.MajorGod)]
    [InlineData("<protomods><unit name='U'><future><conversionprotoid srctype='Old'>Other</conversionprotoid></future></unit></protomods>", GameIdentifierKind.ProtoUnit)]
    public void UnknownOwnerIsNotGrantedNewMigrationPermission(string xml, GameIdentifierKind kind)
    {
        var doc = XDocument.Parse(xml);
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, kind, "Old", "New"));
        Assert.True(XNode.DeepEquals(XDocument.Parse(xml), doc));
        Assert.NotEmpty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(doc, kind, "Old"));
    }

    [Theory]
    [InlineData("<rainspotproto>Old</rainspotproto>", GameIdentifierKind.ProtoUnit)]
    [InlineData("<smallrainspotproto>Old</smallrainspotproto>", GameIdentifierKind.ProtoUnit)]
    [InlineData("<effect type='ProtoActionAdd' unittype='Old' protoaction='Attack'/>", GameIdentifierKind.UnitType)]
    [InlineData("<effect type='EnemyAttachProtoUnit' attachProtoUnit='Old'/>", GameIdentifierKind.UnitType)]
    [InlineData("<birthvfx>Old</birthvfx>", GameIdentifierKind.UnitType)]
    public void ExplicitDomainsDoNotClaimSameNameOtherKinds(string field, GameIdentifierKind kind)
    {
        var doc = XDocument.Parse($"<powersmod><power type='Rain'>{field}</power></powersmod>");
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(doc, kind, "Old", "New"));
        Assert.Empty(GameIdentifierReferencePolicy.FindUnrecognizedReferences(doc, kind, "Old"));
        Assert.Empty(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(doc, kind, "Old"));
    }

    [Fact]
    public void WrappedRainEffectUsesTheSameFieldPolicy()
    {
        var doc = XDocument.Parse("<powersmod><power type='Rain'><effects><effect type='DamageBonus' unittype='Old'/></effects></power></powersmod>");
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(doc, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.Equal("New", (string?)doc.Descendants("effect").Single().Attribute("unittype"));
    }
}
