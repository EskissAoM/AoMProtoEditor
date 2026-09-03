using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyOnHitEffectTests
{
    [Fact]
    public void Normalize_PreservesSignedAmountAndFixesRelativityAndConditionalFields()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="OnHitEffect" effecttype="Attach" amount="0.5" relativity="Percent"
                    modifytype="DamageSpecific" freezetype="stone" progFreezeDuration="4" proto="Wolf" />
            """);

        var changed = TechnologyOnHitEffectRules.Normalize(effect);

        Assert.True(changed);
        Assert.Equal("0.5", (string?)effect.Attribute("amount"));
        Assert.Equal("Assign", (string?)effect.Attribute("relativity"));
        Assert.Null(effect.Attribute("modifytype"));
        Assert.Null(effect.Attribute("freezetype"));
        Assert.Null(effect.Attribute("progFreezeDuration"));
        Assert.Equal("Wolf", (string?)effect.Attribute("proto"));
    }

    [Fact]
    public void Normalize_PreservesNegativeAmountAndDefaultsMissingAmountToZero()
    {
        var negative = XElement.Parse("<effect effecttype=\"Boost\" amount=\"-2.75\" />");
        var missing = XElement.Parse("<effect effecttype=\"Boost\" />");

        TechnologyOnHitEffectRules.Normalize(negative);
        TechnologyOnHitEffectRules.Normalize(missing);

        Assert.Equal("-2.75", (string?)negative.Attribute("amount"));
        Assert.Equal("0", (string?)missing.Attribute("amount"));
        Assert.Equal("Assign", (string?)negative.Attribute("relativity"));
    }

    [Fact]
    public void Normalize_ProvidesFreezeAndProgressiveFreezeDefaults()
    {
        var freeze = XElement.Parse("<effect effecttype=\"Freeze\" />");
        var progressive = XElement.Parse("<effect effecttype=\"ProgFreezeSpeed\" />");

        TechnologyOnHitEffectRules.Normalize(freeze);
        TechnologyOnHitEffectRules.Normalize(progressive);

        Assert.Equal("default", (string?)freeze.Attribute("freezetype"));
        Assert.Equal("1", (string?)progressive.Attribute("progFreezeDuration"));
        Assert.Null(progressive.Attribute("freezetype"));
    }

    [Theory]
    [InlineData("DamageOverTime", null, true)]
    [InlineData("SelfModify", "DamageSpecific", true)]
    [InlineData("StatModify", "ArmorSpecific", true)]
    [InlineData("Boost", "Speed", false)]
    [InlineData("Attach", null, false)]
    public void RequiresDamageType_UsesEffectAndModifyType(string effectType, string? modifyType, bool expected)
    {
        Assert.Equal(expected, TechnologyOnHitEffectRules.RequiresDamageType(effectType, modifyType));
    }

    [Fact]
    public void OptionalButtonRules_RestrictNewFieldsButKeepMutateAlignedWithMutateNature()
    {
        Assert.False(TechnologyOnHitEffectRules.OffersDuration("Lifesteal"));
        Assert.False(TechnologyOnHitEffectRules.OffersDuration("Mutate"));
        Assert.True(TechnologyOnHitEffectRules.OffersDuration("Stun"));
        Assert.True(TechnologyOnHitEffectRules.OffersDamageType("StatModify"));
        Assert.True(TechnologyOnHitEffectRules.OffersDamageType("DamageOverTime"));
        Assert.True(TechnologyOnHitEffectRules.OffersDamageType("Boost"));
        Assert.True(TechnologyOnHitEffectRules.OffersDamageType("SelfModify"));
        Assert.False(TechnologyOnHitEffectRules.OffersDamageType("Stun"));
        Assert.True(TechnologyOnHitEffectRules.OffersProto("Mutate"));
        Assert.True(TechnologyOnHitEffectRules.OffersProto("Root"));
        Assert.False(TechnologyOnHitEffectRules.OffersProto("Stun"));
        Assert.Contains("Mutate", ProtoConstants.KnownOnHitEffectTypes);
        Assert.True(ProtoConstants.IsMutateOnHitEffectType("Mutate"));
        Assert.True(ProtoConstants.IsMutateOnHitEffectType("MutateNature"));
        Assert.Equal("To", TechnologyOnHitEffectRules.GetProtoFieldLabel("Mutate"));
        Assert.Equal("To", TechnologyOnHitEffectRules.GetProtoFieldLabel("MutateNature"));
        Assert.Equal("To", TechnologyOnHitEffectRules.GetProtoFieldLabel("Reincarnation"));
        Assert.Equal("Attach", TechnologyOnHitEffectRules.GetProtoFieldLabel("Attach"));
        Assert.Equal("Spawn", TechnologyOnHitEffectRules.GetProtoFieldLabel("Spawn"));
        Assert.Equal("Protounit", TechnologyOnHitEffectRules.GetProtoFieldLabel("Root"));
    }

    [Fact]
    public void Normalize_PreservesImportedCustomOptionalAttributes()
    {
        var effect = XElement.Parse("""
            <effect effecttype="Stun" duration="3" dmgtype="Divine" proto="ImportedUnit" amount="-1" relativity="Assign" />
            """);

        TechnologyOnHitEffectRules.Normalize(effect);

        Assert.Equal("3", (string?)effect.Attribute("duration"));
        Assert.Equal("Divine", (string?)effect.Attribute("dmgtype"));
        Assert.Equal("ImportedUnit", (string?)effect.Attribute("proto"));
    }

    [Fact]
    public void NormalizeAttributeSubtype_EnforcesFixedActiveAndAttachBoneValues()
    {
        var active = XElement.Parse("<effect amount=\"7\" relativity=\"Percent\" />");
        var disabled = XElement.Parse("<effect amount=\"0\" />");
        var attachBone = XElement.Parse("<effect amount=\"-2\" relativity=\"Absolute\" attachbone=\"spine\" />");

        TechnologyOnHitEffectRules.NormalizeAttributeSubtype(active, "OnHitEffectActive");
        TechnologyOnHitEffectRules.NormalizeAttributeSubtype(disabled, "OnHitEffectActive");
        TechnologyOnHitEffectRules.NormalizeAttributeSubtype(attachBone, "OnHitEffectAttachBone");

        Assert.Equal("1", (string?)active.Attribute("amount"));
        Assert.Equal("Assign", (string?)active.Attribute("relativity"));
        Assert.Equal("0", (string?)disabled.Attribute("amount"));
        Assert.Equal("Assign", (string?)disabled.Attribute("relativity"));
        Assert.Equal("1", (string?)attachBone.Attribute("amount"));
        Assert.Equal("Assign", (string?)attachBone.Attribute("relativity"));
        Assert.Equal("spine", (string?)attachBone.Attribute("attachbone"));
    }

    [Fact]
    public void NormalizeAttributeSubtype_DefaultsEditableValuesAndRateDamageType()
    {
        var rate = XElement.Parse("<effect effecttype=\"DamageOverTime\" />");
        var probability = XElement.Parse("<effect />");

        TechnologyOnHitEffectRules.NormalizeAttributeSubtype(rate, "OnHitEffectRate");
        TechnologyOnHitEffectRules.NormalizeAttributeSubtype(probability, "OnHitEffectProbability");

        Assert.Equal("0", (string?)rate.Attribute("amount"));
        Assert.Equal("BasePercent", (string?)rate.Attribute("relativity"));
        Assert.Equal("All", (string?)rate.Attribute("dmgtype"));
        Assert.Equal("0", (string?)probability.Attribute("amount"));
        Assert.Equal("BasePercent", (string?)probability.Attribute("relativity"));
    }

    [Fact]
    public void AttributeSubtypeCatalogs_RestrictStatModifyAndEditableAmountFamilies()
    {
        Assert.Equal(new[] { "StatModify", "Boost", "SelfModify" }, TechnologyOnHitEffectRules.StatModifyEffectTypes);
        Assert.True(TechnologyOnHitEffectRules.UsesEditableAmount("OnHitEffectDuration"));
        Assert.True(TechnologyOnHitEffectRules.UsesEditableAmount("OnHitEffectProbability"));
        Assert.True(TechnologyOnHitEffectRules.UsesEditableAmount("OnHitEffectRate"));
        Assert.True(TechnologyOnHitEffectRules.UsesEditableAmount("OnHitEffectStatModify"));
        Assert.False(TechnologyOnHitEffectRules.UsesEditableAmount("OnHitEffectActive"));
    }

    [Fact]
    public void TechnologyEditor_UsesSharedOnHitCatalogsAndStructuredConditionalEditors()
    {
        var root = FindProjectRoot();
        var technologyCode = File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
        var protoCode = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("AddOnHitEffectDataEffectEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("AddOnHitEffectAttributeDataEffectEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("OnHitEffectAttributeDataSubtypes", technologyCode, StringComparison.Ordinal);
        Assert.Contains("CreateOnHitEffectActiveStateCombo", technologyCode, StringComparison.Ordinal);
        Assert.Contains("CreateExplicitOnHitDamageTypeCombo", technologyCode, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"attachbone\")", technologyCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyOnHitEffectRules.StatModifyEffectTypes", technologyCode, StringComparison.Ordinal);
        Assert.Contains("TechnologyOnHitEffectRules.GetProtoFieldLabel(effectType)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("buttonLeftSpacing: 8", technologyCode, StringComparison.Ordinal);
        Assert.Contains("var damageTypeCombo = new ComboBox", technologyCode, StringComparison.Ordinal);
        Assert.Contains("selectAllOnFirstClick: true", technologyCode, StringComparison.Ordinal);
        Assert.Contains("normalized.Equals(lastCommittedValue, StringComparison.OrdinalIgnoreCase)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownOnHitEffectTypes", technologyCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.KnownModifyTypes", technologyCode, StringComparison.Ordinal);
        Assert.Contains("AddOnHitOptionalReferenceSelector", technologyCode, StringComparison.Ordinal);
        Assert.Contains("AddOnHitDamageTypeEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("AddOnHitFreezeTypeEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("AddOnHitProgressiveFreezeDurationEditor", technologyCode, StringComparison.Ordinal);
        Assert.Contains("CreateSignedFloatEffectBox(effect, \"amount\"", technologyCode, StringComparison.Ordinal);
        Assert.Contains("if (value.Equals(effectType, StringComparison.OrdinalIgnoreCase)) return;", technologyCode, StringComparison.Ordinal);
        Assert.Contains("_openOnHitOptionalSelectors.RemoveWhere(entry => ReferenceEquals(entry.Effect, effect));", technologyCode, StringComparison.Ordinal);
        Assert.Contains("offerWhenMissing: TechnologyOnHitEffectRules.OffersDuration(effectType)", technologyCode, StringComparison.Ordinal);
        Assert.Contains("!showWhenMissing && (attribute != null || _openOnHitOptionalSelectors.Contains(key))", technologyCode, StringComparison.Ordinal);
        Assert.Contains("SupportedOnHitEffectTypes = ProtoConstants.KnownOnHitEffectTypes", protoCode, StringComparison.Ordinal);
        Assert.Contains("KnownOnHitEffectFreezeTypes = ProtoConstants.KnownOnHitEffectFreezeTypes", protoCode, StringComparison.Ordinal);
        Assert.Contains("ProtoConstants.IsMutateOnHitEffectType(currentSupportedType)", protoCode, StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AoMDivineDataEditor.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AoMDivineDataEditor.csproj.");
    }
}
