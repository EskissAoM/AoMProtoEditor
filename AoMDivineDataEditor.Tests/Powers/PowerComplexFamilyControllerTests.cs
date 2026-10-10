using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerComplexFamilyControllerTests
{
    [Fact]
    public void EffectFamilyOwnsDefaultsRelativityAndTargetNormalization()
    {
        var armor = new XElement("effect");
        PowerEffectFamilyController.InitializeForType(armor, "ArmorVulnerability");
        Assert.Equal("Hack", armor.Attribute("armortype")?.Value);
        Assert.Equal("BasePercent", armor.Attribute("relativity")?.Value);
        Assert.Equal("0", armor.Attribute("amount")?.Value);
        Assert.DoesNotContain("Set to",
            PowerEffectFamilyController.GetRelativityDisplays("ArmorVulnerability", "BasePercent"));

        var tactic = new XElement("effect");
        PowerEffectFamilyController.InitializeForType(tactic, "TacticEnable");
        Assert.Empty(tactic.Attributes());
        Assert.False(PowerEffectFamilyController.UsesStandardAmount("TacticEnable"));
        Assert.True(PowerEffectFamilyController.SupportsIgnoreNature("tacticenable"));

        var effect = XElement.Parse("""
            <effect>
              <target type="ProtoUnit">OldTarget</target>
              <target type="ProtoUnit">Duplicate</target>
            </effect>
            """);
        PowerEffectFamilyController.NormalizeTargetForType(effect, "EnemyAttachProtoUnit");
        var target = Assert.Single(effect.Elements("target"));
        Assert.Equal("Player", target.Attribute("type")?.Value);
        Assert.Empty(target.Value);

        Assert.Equal("Multiply base", PowerEffectFamilyController.RelativityToDisplay("BasePercent"));
        Assert.Equal("Assign", PowerEffectFamilyController.DisplayToRelativity("Set to"));
        Assert.True(PowerEffectFamilyController.IsSharedPowerType("eclipse"));
    }

    [Fact]
    public void EffectTypeTransitionPreservesOnlyTheTargetOfAPreviouslyUntypedEffect()
    {
        var incomplete = XElement.Parse("""
            <effect amount="9" hidetooltip="">
              <target type="ProtoUnit">Soldier</target>
              <moddedChild>discard</moddedChild>
            </effect>
            """);

        PowerEffectFamilyController.ChangeType(incomplete, "", "CannotSnare");

        Assert.Equal("CannotSnare", incomplete.Attribute("type")?.Value);
        Assert.NotNull(incomplete.Attribute("hidetooltip"));
        Assert.Null(incomplete.Attribute("amount"));
        var preservedTarget = Assert.Single(incomplete.Elements("target"));
        Assert.Equal("ProtoUnit", preservedTarget.Attribute("type")?.Value);
        Assert.Equal("Soldier", preservedTarget.Value);
        Assert.Null(incomplete.Element("moddedChild"));

        var typed = XElement.Parse("""
            <effect type="WorkRate" relativity="BasePercent" amount="2" norevert="">
              <target type="ProtoUnit">Soldier</target>
            </effect>
            """);

        PowerEffectFamilyController.ChangeType(typed, "WorkRate", "TechActivate");

        Assert.Equal("TechActivate", typed.Attribute("type")?.Value);
        Assert.NotNull(typed.Attribute("norevert"));
        Assert.Null(typed.Attribute("relativity"));
        Assert.Null(typed.Attribute("amount"));
        var normalizedTarget = Assert.Single(typed.Elements("target"));
        Assert.Equal("Tech", normalizedTarget.Attribute("type")?.Value);
        Assert.Empty(normalizedTarget.Value);
    }

    [Fact]
    public void EffectInAreaOwnsOnHitOrderingAndDefaults()
    {
        Assert.True(EffectInAreaFamilyController.IsPower(
            new XElement("power", new XAttribute("type", "effectinarea"))));
        Assert.True(EffectInAreaFamilyController.IsSupportedOnHitType("Freeze"));
        Assert.Contains("proto", EffectInAreaFamilyController.GetOnHitAttributeOrder("attach"));
        Assert.Contains("attachbone", EffectInAreaFamilyController.GetOnHitAttributeOrder("attach"));

        var freeze = new XElement("onhiteffect");
        EffectInAreaFamilyController.SeedOnHitDefaults(freeze, "Freeze");
        Assert.Equal(ProtoConstants.KnownOnHitEffectFreezeTypes[0], freeze.Attribute("freezetype")?.Value);

        var chance = new XElement("onhiteffect");
        EffectInAreaFamilyController.SeedOnHitDefaults(chance, "InstantKillablePercentChance");
        Assert.Equal("Set", chance.Attribute("relativity")?.Value);
    }

    [Fact]
    public void UnitModifyOwnsFamilyCapabilitiesVfxTagsAndDefaultEntry()
    {
        Assert.True(UnitModifyFamilyController.IsPowerType("SingleModify"));
        Assert.True(UnitModifyFamilyController.IsPowerType("NewMoon"));
        Assert.False(UnitModifyFamilyController.SupportsKillOnEffectEnd("NewMoon"));
        Assert.True(UnitModifyFamilyController.SupportsKillOnEffectEnd("UnitModify"));

        Assert.Equal(
            new UnitModifyVfxTags("modelattachment", "modelattachmentbone", null),
            UnitModifyFamilyController.GetVfxTags("NewMoon"));
        Assert.Equal(
            new UnitModifyVfxTags("vfxproto", "vfxattachbone", "birthvfx"),
            UnitModifyFamilyController.GetVfxTags("SingleModify"));

        var power = new XElement("power");
        var modify = UnitModifyFamilyController.AddDefaultModify(power);
        Assert.Same(modify, Assert.Single(power.Elements("unitmodify")));
        Assert.Equal("Add", modify.Attribute("apply")?.Value);
        Assert.Equal("0", modify.Attribute("amount")?.Value);
    }

    [Fact]
    public void SwitchingEffectsOwnsTierTransitionsAndGeneratedStringIdentity()
    {
        var power = XElement.Parse("""
            <power name="BushidoAmaterasu" type="SwitchingEffects">
              <tiertype>Time</tiertype>
              <interval>1</interval>
              <effects><effect /></effects>
            </power>
            """);

        Assert.Equal("STR_SWITCHING_EFFECT_BUSHIDO_AMATERASU_NAME_1",
            SwitchingEffectsFamilyController.CreateStringId("BushidoAmaterasu", "NAME", 1));
        Assert.True(SwitchingEffectsFamilyController.IsOwnedStringId(
            "BushidoAmaterasu", "str_switching_effect_bushido_amaterasu_lr"));

        SwitchingEffectsFamilyController.ApplyTierType(power, "TotalResources");
        Assert.Null(PowerCatalog.FindField(power, "interval"));
        var resource = Assert.Single(PowerCatalog.FindField(power, "effects")!.Elements("resourcetierreq"));
        Assert.Equal("Food", resource.Attribute("type")?.Value);

        var creation = SwitchingEffectsFamilyController.AddEffectContainer(
            power, "BushidoAmaterasu", "CombatXP");
        Assert.Equal(3, creation.StringIds.Count);
        Assert.NotNull(PowerCatalog.FindField(creation.Container, "combatxptierreq"));
        Assert.NotNull(PowerCatalog.FindField(creation.Container, "effect"));
        Assert.All(creation.StringIds, id => Assert.True(
            SwitchingEffectsFamilyController.IsOwnedStringId("BushidoAmaterasu", id)));

        SwitchingEffectsFamilyController.ApplyTierType(power, "CombatXP");
        Assert.All(power.Elements("effects"), effects =>
        {
            Assert.Null(PowerCatalog.FindField(effects, "resourcetierreq"));
            Assert.NotNull(PowerCatalog.FindField(effects, "combatxptierreq"));
        });

        var tooltip = SwitchingEffectsFamilyController.AddExtraTooltip(
            power, creation.Container, "BushidoAmaterasu");
        Assert.Equal("STR_SWITCHING_EFFECT_BUSHIDO_AMATERASU_TOOLTIP", tooltip.Value);
        Assert.Contains(tooltip.Value,
            SwitchingEffectsFamilyController.EnumerateOwnedStringIds(
                creation.Container, "BushidoAmaterasu"));

        PowerCatalog.SetField(power, "timerrolloverid",
            SwitchingEffectsFamilyController.CreateStringId("BushidoAmaterasu", "PROG"));
        SwitchingEffectsFamilyController.ApplyProgressType(power, "Hide");
        Assert.NotNull(PowerCatalog.FindField(power, "hidewheelview"));
        Assert.Null(PowerCatalog.FindField(power, "timerrolloverid"));
    }
}
