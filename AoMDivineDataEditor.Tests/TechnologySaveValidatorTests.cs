using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologySaveValidatorTests
{
    private static readonly TechnologySaveCatalogs Catalogs = new(
        ["Damage", "Resource", "ActionEnable", "PowerROF"],
        ["CreatePower", "ModifyProtoUnit", "ForbidTech"],
        ["ExistingTech", "CustomTech"],
        ["Archer", "Infantry"],
        ["Archer"],
        ["AgeUpgrade"],
        ["RangedAttack"],
        ["Default"],
        ["Meteor"],
        ["Greek"],
        ["Zeus"],
        ["weapon"],
        ["Delete"]);

    [Fact]
    public void ValidKnownAndUnknownEffectsCanBeSavedTogether()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="CustomTech">
                <researchpoints>10</researchpoints>
                <techtype>AgeUpgrade</techtype>
                <effects>
                  <effect type="Data" subtype="Damage" action="RangedAttack" amount="1.5" relativity="Percent">
                    <target type="ProtoUnit">Archer</target>
                  </effect>
                  <effect type="FutureCustomEffect" arbitrary="preserved" />
                  <effect type="Data" subtype="FutureCustomSubtype" arbitrary="preserved" />
                </effects>
              </tech>
            </techtreemods>
            """);

        var result = TechnologySaveValidator.Validate(document, Catalogs);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void MissingSelectorsInvalidReferencesAndInvalidNumbersAreReportedTogether()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="Bad Tech">
                <researchpoints>ten</researchpoints>
                <effects>
                  <effect type="Data" subtype="ActionEnable" amount="not-a-number" relativity="Assign">
                    <target type="ProtoUnit">MissingUnit</target>
                  </effect>
                  <effect type="Data" subtype="PowerROF" amount="1" relativity="Absolute" protopower="MissingPower">
                    <target type="Player" />
                  </effect>
                </effects>
              </tech>
            </techtreemods>
            """);

        var result = TechnologySaveValidator.Validate(document, Catalogs);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("invalid internal name", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("<researchpoints>", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("requires a concrete Action", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("MissingUnit", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("MissingPower", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("valid number", StringComparison.Ordinal));
    }

    [Fact]
    public void PositiveAndIntegerRangesAreValidated()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="CustomTech">
                <effects>
                  <effect type="CreatePower" protoPower="Meteor" cooldown="0" select="1.5" />
                </effects>
              </tech>
            </techtreemods>
            """);

        var result = TechnologySaveValidator.Validate(document, Catalogs);

        Assert.Contains(result.Errors, error => error.Contains("cooldown", StringComparison.Ordinal) && error.Contains("greater than 0", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("select", StringComparison.Ordinal) && error.Contains("integer", StringComparison.Ordinal));
    }

    [Fact]
    public void StructuredAndLegacyPrerequisitesUseTheirRealXmlValues()
    {
        var document = XDocument.Parse("""
            <techtreemods>
              <tech name="CustomTech">
                <prereqs>
                  <specificage>MythicAge</specificage>
                  <techstatus status="Unobtainable">ExistingTech</techstatus>
                  <typecount unit="Infantry" count="12.00" state="nonExistState" operator="ne" />
                  <KBStat kbStat="tradeProfit" value="0" operator="gte" kbParam="Gold" />
                  <culture><cultureName>Greek</cultureName></culture>
                  <civilization>Zeus</civilization>
                </prereqs>
              </tech>
            </techtreemods>
            """);

        var result = TechnologySaveValidator.Validate(document, Catalogs);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Generated_string_namespace_collision_blocks_save()
    {
        var document = XDocument.Parse("<techtreemods><tech name=\"A_B\" /></techtreemods>");
        var catalogs = Catalogs with { TechnologyNames = ["ExistingTech", "A-B", "A_B"] };

        var result = TechnologySaveValidator.Validate(document, catalogs);

        Assert.Contains(result.Errors, error =>
            error.Contains("same string ID namespace", StringComparison.OrdinalIgnoreCase));
    }
}
