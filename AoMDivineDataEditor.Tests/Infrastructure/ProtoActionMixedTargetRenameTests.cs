using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionMixedTargetRenameTests
{
    private static XDocument Document(string field, bool standalone = false)
        => XDocument.Parse(standalone
            ? $"<tactics><action><name>Attack</name><type>Old</type><{field} type='Old' keep='yes'>2</{field}></action></tactics>"
            : $"<protomods><unit name='Old'><future type='Old'/></unit><unit name='Other'><protoaction><name>Attack</name><type>Old</type><{field} type='Old' keep='yes'>2</{field}></protoaction></unit></protomods>");

    [Theory]
    [InlineData("rate", GameIdentifierKind.ProtoUnit)]
    [InlineData("rate", GameIdentifierKind.UnitType)]
    [InlineData("damagebonus", GameIdentifierKind.ProtoUnit)]
    [InlineData("damagebonus", GameIdentifierKind.UnitType)]
    public void ActionTargetParticipatesInBothRenameDomains(string field, GameIdentifierKind kind)
    {
        var document = Document(field);
        Assert.True(GameIdentifierReferencePolicy.HasMixedUnitReference(document, "Old"));
        Assert.Single(GameIdentifierReferencePolicy.FindRecognizedReferences(document, kind, "Old"));
        var unsupported = GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, kind, "Old");
        // The deliberate unrelated Old-valued type fields stay conservative
        // blockers for UnitType migration; neither is the managed target field.
        if (kind == GameIdentifierKind.UnitType) Assert.Equal(["@type", "<type>"], unsupported);
        else Assert.Empty(unsupported);
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, kind, "Old", "New"));
        var target = document.Descendants(field).Single();
        Assert.Equal("New", (string?)target.Attribute("type"));
        Assert.Equal("2", target.Value);
        Assert.Equal("yes", (string?)target.Attribute("keep"));
        Assert.Equal("Old", document.Descendants("type").Single().Value); // Action type is a different domain.
        Assert.Equal("Old", (string?)document.Descendants("future").Single().Attribute("type"));
        Assert.Equal("Old", (string?)document.Root!.Element("unit")!.Attribute("name"));
    }

    [Theory]
    [InlineData("rate", "Old")]
    [InlineData("rate", "New")]
    [InlineData("rate", null)]
    [InlineData("damagebonus", "Old")]
    [InlineData("damagebonus", "New")]
    [InlineData("damagebonus", null)]
    public void UnitRenameRejectsAmbiguousOrUnavailableOppositeCatalog(string field, string? collision)
    {
        var document = Document(field);
        var before = document.ToString(SaveOptions.DisableFormatting);
        var error = Assert.Throws<InvalidOperationException>(() =>
            GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(document,
                GameIdentifierKind.ProtoUnit, "Old", "New", collision == null ? null : [collision], "Unit draft"));
        Assert.Contains("mixed Unit/Unit Type", error.Message);
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("damagebonus")]
    public void StandaloneActionUsesSameIdentityAndPreservationRules(string field)
    {
        var document = Document(field, standalone: true);
        GameIdentifierReferenceMigrationPlan.ValidateUnitReferenceIdentity(document,
            GameIdentifierKind.ProtoUnit, "Old", "New", ["UnrelatedType"], "Tactics");
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.ProtoUnit, "Old", "New"));
        Assert.Equal("New", (string?)document.Descendants(field).Single().Attribute("type"));
        Assert.Equal("Old", document.Descendants("type").Single().Value);
    }
}
