using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerSaveValidatorTests
{
    private static readonly PowerSaveCatalogs Catalogs =
        new(PowerEditorAuditInventory.EditorPowerTypes);

    [Fact]
    public void ValidatesEveryKnownPowerRecordWithPathAwareErrors()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="BrokenRain" type="Rain" orderhint="not-a-number">
                <radius>-1</radius>
                <effect amount="bad" />
              </power>
              <power name="BrokenSwitch" type="SwitchingEffects">
                <tiertype>CombatXP</tiertype>
                <effects>
                  <combatxptierreq>-2</combatxptierreq>
                  <effect type="Damage" amount="NaN" />
                </effects>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, Catalogs);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("BrokenRain", StringComparison.Ordinal) &&
                                                error.Contains("@orderhint", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("BrokenRain", StringComparison.Ordinal) &&
                                                error.Contains("/radius", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("BrokenRain", StringComparison.Ordinal) &&
                                                error.Contains("effect/@type", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("BrokenSwitch", StringComparison.Ordinal) &&
                                                error.Contains("combatxptierreq", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("BrokenSwitch", StringComparison.Ordinal) &&
                                                error.Contains("@amount", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownPowerTypesAndExcludedAbilityRecordsRemainLossless()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="FuturePower" type="ModdedFutureType" orderhint="legacy-value">
                <effect amount="not-our-schema" />
                <futurefield arbitrary="true" amount="not-our-schema">
                  <radius>not-our-radius</radius><vfxproto>not-our-vfx</vfxproto>
                </futurefield>
              </power>
              <power name="LegacyAction" type="UnitAction">
                <effect />
              </power>
              <power name="LegacyGeneral" type="GeneralEffect">
                <effect />
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, Catalogs);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void UnknownNestedFieldsInsideAKnownPowerAreNotClaimedByLeafName()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="KnownWithFutureData" type="Bolt">
                <futurecontainer amount="mod-value">
                  <radius>mod-radius</radius>
                  <vfxproto>mod-vfx</vfxproto>
                </futurecontainer>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, FullCatalogs());

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void RequiresPowerAndEffectTypesForEditorOwnedStructures()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="MissingPowerType" />
              <power name="Area" type="EffectInArea">
                <onhiteffect />
              </power>
              <power name="Switch" type="SwitchingEffects">
                <interval>1</interval>
                <effects><effect /></effects>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, Catalogs);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Contains("MissingPowerType", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("onhiteffect[1]/@type", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("effects[1]/effect/@type", StringComparison.Ordinal));
    }

    [Fact]
    public void PowerCloneCostsAcceptDecimalsWhileOtherPowerCostsRequireWholeNumbers()
    {
        var validClone = XDocument.Parse("""
            <powersmod><power name="Clone" type="PowerClone"><cost>1.5</cost><repeatcost>2.25</repeatcost></power></powersmod>
            """);
        var invalidRain = XDocument.Parse("""
            <powersmod><power name="RainCost" type="Rain"><cost>1.5</cost></power></powersmod>
            """);

        Assert.True(PowerSaveValidator.Validate(validClone, Catalogs).IsValid);
        var result = PowerSaveValidator.Validate(invalidRain, Catalogs);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("whole number", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyOptionalNumericElementsThatCarryAttributesRemainValid()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="NotificationOnly" type="Bolt">
                <minimapeventtime sendalertto="None" />
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, Catalogs);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void StructuralAmbiguityStopsSchemaValidation()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Duplicate" type="Rain" />
              <power name="Duplicate" type="Rain" />
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, Catalogs);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatesUnitModifyShapeCatalogsAndSpecificDamageType()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Modify" type="UnitModify">
                <unitmodify apply="Wrong" amount="bad">UnknownModify</unitmodify>
                <unitmodify apply="Add" amount="2">DamageSpecific</unitmodify>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, FullCatalogs());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unknown Modify Type", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Add, Multiply, or Set", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("@amount", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("@damagetype is required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesPowerEffectSubtypeShapeAndOmissionRules()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Effects" type="Rain">
                <effect type="ActionEnable"><target type="ProtoUnit">UnitA</target></effect>
                <effect type="ArmorVulnerability" relativity="Assign" amount="1" armortype="All">
                  <target type="ProtoUnit">UnitA</target>
                </effect>
                <effect type="Damage" relativity="BasePercent" amount="1" damagetype="All" allactions="1">
                  <target type="ProtoUnit">UnitA</target>
                </effect>
                <effect type="EnemyAttachProtoUnit"><target type="Player" /></effect>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, FullCatalogs());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("/@action", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("unknown catalog value 'All'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("cannot be Assign", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("must be omitted", StringComparison.Ordinal) &&
                                                error.Contains("damagetype", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("allactions must be omitted", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("attachProtoUnit", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("unittype", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesRepeatedAgeAndExclusiveResourceContainers()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Gift" type="FreyrsGift">
                <createagespecificunit age="ArchaicAge" quantity="1">UnitA</createagespecificunit>
                <createagespecificunit age="ArchaicAge" quantity="two">UnitA</createagespecificunit>
              </power>
              <power name="Charm" type="FoxCharm">
                <ageadjustment affectedNumber="1">ArchaicAge</ageadjustment>
                <ageadjustment affectedNumber="2">ArchaicAge</ageadjustment>
              </power>
              <power name="Cage" type="CageUnit">
                <resourcevaluemultiplier resourcetype="Food">1</resourcevaluemultiplier>
                <resourcevaluemultiplier resourcetype="Food">2</resourcevaluemultiplier>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, FullCatalogs());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("duplicate age", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("quantity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("duplicate value 'ArchaicAge'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("duplicate resourcetype 'Food'", StringComparison.Ordinal));
    }

    [Fact]
    public void NewCatalogValuesAreStrictButUnchangedLegacyValuesRemainSaveable()
    {
        var legacyPower = XElement.Parse("""
            <power name="Legacy" type="Curse"><vfxproto>ModOnlyVfx</vfxproto></power>
            """);
        var catalogs = FullCatalogs([legacyPower]);
        var preserved = XDocument.Parse("""
            <powersmod><power name="RenamedLegacy" type="Curse"><vfxproto>ModOnlyVfx</vfxproto></power></powersmod>
            """);
        var invented = XDocument.Parse("""
            <powersmod><power name="New" type="Curse"><vfxproto>InventedVfx</vfxproto></power></powersmod>
            """);

        Assert.True(PowerSaveValidator.Validate(preserved, catalogs).IsValid);
        var result = PowerSaveValidator.Validate(invented, catalogs);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("InventedVfx", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatesSwitchingProgressAndTierCrossFieldRules()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Timed" type="SwitchingEffects"><tiertype>Time</tiertype></power>
              <power name="Hidden" type="SwitchingEffects">
                <tiertype>CombatXP</tiertype><hidewheelview /><timerrolloverid>STR_TIMER</timerrolloverid>
                <effects><combatxptierreq>-1</combatxptierreq></effects>
              </power>
            </powersmod>
            """);

        var result = PowerSaveValidator.Validate(document, FullCatalogs());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("interval is required", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("timerrolloverid must be omitted", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("combatxptierreq", StringComparison.Ordinal) &&
                                                error.Contains("less than 0", StringComparison.Ordinal));
    }

    [Fact]
    public void TerrainCreepRequiresAnExplicitCatalogSelection()
    {
        var empty = XDocument.Parse("""
            <powersmod><power name="EmptyCreep" type="Wither">
              <terraincreep creep="" maxradius="0" />
            </power></powersmod>
            """);
        var selected = XDocument.Parse("""
            <powersmod><power name="SelectedCreep" type="Wither">
              <terraincreep creep="CreepA" maxradius="0" />
            </power></powersmod>
            """);

        var invalid = PowerSaveValidator.Validate(empty, FullCatalogs());

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error =>
            error.Contains("/terraincreep/@creep", StringComparison.Ordinal));
        Assert.True(PowerSaveValidator.Validate(selected, FullCatalogs()).IsValid);
    }

    private static PowerSaveCatalogs FullCatalogs(IReadOnlyCollection<XElement>? legacy = null)
        => new(
            PowerEditorAuditInventory.EditorPowerTypes,
            ["UnitA", "UnitType"],
            ["UnitA", "VfxA"],
            ["AttackA"],
            ["TechA"],
            ["AnimA"],
            ["BoneA"],
            ["Land"],
            ["PowerA"],
            legacy ?? [],
            ["CreepA"]);
}
