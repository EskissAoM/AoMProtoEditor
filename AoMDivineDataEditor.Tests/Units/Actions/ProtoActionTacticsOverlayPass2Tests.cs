using System.Xml.Linq;
using System.Reflection;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionTacticsOverlayPass2Tests
{
    [Fact]
    public async Task TacticsSave_CancelledCallbackDoesNotReportCommit()
    {
        var callbackInvoked = false;
        var document = XDocument.Parse("<tactics />");
        var session = new TacticsActionEditorSession(
            "test.tactics",
            isReadOnly: false,
            document,
            _ =>
            {
                callbackInvoked = true;
                return Task.FromResult(TacticsDocumentSaveOutcome.Cancelled);
            });
        var committed = await session.TrySaveAsync(document);

        Assert.True(callbackInvoked);
        Assert.Equal(TacticsEditorSaveResult.Cancelled, committed);
    }

    [Fact]
    public void AdditionalMerge_ReplacesInheritedTagButNeverProtoSiblings()
    {
        var inherited = Elements(
            "<charged><activationtype>AfterAction</activationtype></charged>",
            "<persistent>1</persistent>");
        var proto = Elements(
            "<charged><chargedmodelattachment>success.xml</chargedmodelattachment></charged>",
            "<charged><chargedmodelattachment>failure.xml</chargedmodelattachment></charged>",
            "<persistent>0</persistent>");

        var merged = ProtoEditorWindow.MergeProtoActionAdditionalElements(inherited, proto);

        Assert.Equal(2, merged.Count(element => element.Name.LocalName == "charged"));
        Assert.Equal(
            ["success.xml", "failure.xml"],
            merged.Where(element => element.Name.LocalName == "charged")
                .Select(element => element.Element("chargedmodelattachment")?.Value));
        Assert.Equal("0", Assert.Single(merged, element => element.Name.LocalName == "persistent").Value);
    }

    [Fact]
    public void AdditionalMerge_RateOverridesByTypeAndPreservesOtherTypes()
    {
        var inherited = Elements(
            "<rate type=\"Food\" resource=\"Food\">1</rate>",
            "<rate type=\"Wood\" resource=\"Wood\">2</rate>");
        var proto = Elements(
            "<rate type=\"food\" resource=\"Favor\">3</rate>",
            "<rate type=\"Gold\">4</rate>");

        var merged = ProtoEditorWindow.MergeProtoActionAdditionalElements(inherited, proto)
            .Where(element => element.Name.LocalName == "rate")
            .ToList();

        Assert.Equal(3, merged.Count);
        Assert.Equal(["Wood", "food", "Gold"], merged.Select(element => (string?)element.Attribute("type")));
        Assert.Equal(["2", "3", "4"], merged.Select(element => element.Value));
    }

    [Fact]
    public void AdditionalMerge_SameTypeProtoOnHitEffectsRemainSeparate()
    {
        var inherited = Elements("<onhiteffect type=\"Modifier\" amount=\"1\" />");
        var proto = Elements(
            "<onhiteffect type=\"Modifier\" amount=\"2\" />",
            "<onhiteffect type=\"Modifier\" amount=\"3\" />");

        var merged = ProtoEditorWindow.MergeProtoActionAdditionalElements(inherited, proto);

        Assert.Equal(2, merged.Count);
        Assert.Equal(["2", "3"], merged.Select(element => (string?)element.Attribute("amount")));
    }

    [Fact]
    public void OverrideIndicator_DoesNotTreatProtoOnlyTagsOrKeysAsOverrides()
    {
        var tactics = new ProtoAction { Name = "Attack", Type = "Attack" };

        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(
            new ProtoAction { Name = "Attack", MaxRange = "12" }, tactics));

        var keyedAddition = new ProtoAction { Name = "Attack" };
        keyedAddition.AdditionalElements.Add(XElement.Parse("<rate type=\"Food\">1</rate>"));
        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(keyedAddition, tactics));

        tactics.AdditionalElements.Add(XElement.Parse("<modifyabstracttype>AbstractArcher</modifyabstracttype>"));
        var collectionAddition = new ProtoAction { Name = "Attack" };
        collectionAddition.AdditionalElements.Add(XElement.Parse("<modifyabstracttype>AbstractCavalry</modifyabstracttype>"));
        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(collectionAddition, tactics));
    }

    [Fact]
    public void OverrideIndicator_DetectsChangedInheritedTagsAndKeys()
    {
        var tactics = new ProtoAction { Name = "Attack", Type = "Attack", MaxRange = "10" };
        tactics.Damages.Add(("Hack", "5"));
        tactics.AdditionalElements.Add(XElement.Parse("<rate type=\"Food\">1</rate>"));

        Assert.True(ProtoEditorWindow.HasActualTacticsOverride(
            new ProtoAction { Name = "Attack", MaxRange = "12" }, tactics));

        var keyedOverride = new ProtoAction { Name = "Attack" };
        keyedOverride.AdditionalElements.Add(XElement.Parse("<rate type=\"food\">2</rate>"));
        Assert.True(ProtoEditorWindow.HasActualTacticsOverride(keyedOverride, tactics));

        var damageOverride = new ProtoAction { Name = "Attack" };
        damageOverride.Damages.Add(("Hack", "6"));
        Assert.True(ProtoEditorWindow.HasActualTacticsOverride(damageOverride, tactics));
    }

    [Fact]
    public void OverrideIndicator_IgnoresSemanticallyRedundantNumericOverlay()
    {
        var tactics = new ProtoAction { Name = "Attack", Type = "Attack", Rof = "1.000" };
        tactics.AdditionalElements.Add(XElement.Parse("<rate type=\"Food\">2.0</rate>"));
        var proto = new ProtoAction { Name = "Attack", Rof = "1" };
        proto.AdditionalElements.Add(XElement.Parse("<rate type=\"food\">2</rate>"));

        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(proto, tactics));
    }

    [Fact]
    public void OverrideIndicator_NameOnlyProtoActionIsInheritedNotOverridden()
    {
        var tactics = new ProtoAction { Name = "HandAttack", Type = "Attack", Rof = "1" };
        var nameOnlyProtoAction = new ProtoAction { Name = "HandAttack" };

        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(nameOnlyProtoAction, tactics));
    }

    [Fact]
    public void OverrideIndicator_HandAttackProtoOnlyPayloadIsNotAnOverride()
    {
        var tactics = new ProtoAction { Name = "HandAttack", Type = "Attack" };
        tactics.AdditionalElements.Add(XElement.Parse("<rate type=\"LogicalTypeHandUnitsAttack\">1</rate>"));
        tactics.AdditionalElements.Add(XElement.Parse("<attackaction>1</attackaction>"));
        tactics.AdditionalElements.Add(XElement.Parse("<handlogic>1</handlogic>"));
        tactics.AdditionalElements.Add(XElement.Parse("<forceareaattacktarget>1</forceareaattacktarget>"));
        tactics.AdditionalElements.Add(XElement.Parse("<anim>HandAttack</anim>"));
        tactics.AdditionalElements.Add(XElement.Parse("<onhiteffect type=\"Snare\" duration=\"2\" rate=\"0.85\" />"));
        tactics.AdditionalElements.Add(XElement.Parse("<impacteffect>effects\\impacts\\hack\\</impacteffect>"));

        var proto = new ProtoAction { Name = "HandAttack", Rof = "1", MaxRange = "0.75" };
        proto.Damages.Add(("Hack", "9"));
        proto.DamageBonuses.Add(("MythUnit", "8"));
        proto.AdditionalElements.Add(XElement.Parse("<defaultattack>1</defaultattack>"));

        Assert.False(ProtoEditorWindow.HasActualTacticsOverride(proto, tactics));
    }

    [Theory]
    [InlineData(ProtoUnitNumericKind.UnsignedFloat)]
    [InlineData(ProtoUnitNumericKind.ClampZeroToOne)]
    public void SpawnOptionalNumericFields_AcceptEmptyValues(ProtoUnitNumericKind kind)
    {
        var rule = ProtoEditorWindow.CreateOptionalSpawnNumericRule("Optional Spawn field", kind);

        Assert.True(ProtoUnitStatsNumericRules.Validate("", rule).IsValid);
    }

    [Fact]
    public void StatsValidationReport_IdentifiesTheFailingProtoUnit()
    {
        var report = ProtoEditorWindow.BuildProtoUnitStatsValidationReport(
            "PeachBlossomCopy",
            ["Numeric fields:\n• Invalid value"]);

        Assert.Contains("ProtoUnit: PeachBlossomCopy", report, StringComparison.Ordinal);
        Assert.Contains("Invalid value", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_ConversionProtoIdRemainsInTheManagedStandardConvertLayout()
    {
        var profilesField = typeof(ProtoActionMetadataCatalog).GetField(
            "EditorProfiles",
            BindingFlags.NonPublic | BindingFlags.Static);
        var profiles = Assert.IsAssignableFrom<IReadOnlyDictionary<string, ProtoActionTypeEditorProfile>>(
            profilesField?.GetValue(null));
        var convert = profiles["Convert"];

        Assert.Contains("conversionprotoid", convert.DefaultVisibleTags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("conversionprotoid", convert.HiddenByDefaultTags, StringComparer.OrdinalIgnoreCase);
        Assert.True(ProtoEditorWindow.SupportsRateLinkedConversion(isCharmedConvert: false));
        Assert.False(ProtoEditorWindow.SupportsRateLinkedConversion(isCharmedConvert: true));
    }

    [Fact]
    public void TacticsParser_RejectsDuplicateActionNamesCaseInsensitively()
    {
        var error = Assert.Throws<InvalidDataException>(() => ProtoEditorWindow.ParseTacticsActions("""
            <tactics>
              <action><name>HandAttack</name><type>Attack</type></action>
              <action><name>handattack</name><type>Heal</type></action>
            </tactics>
            """));

        Assert.Contains("duplicate action name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HandAttack", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TacticsParser_IgnoresUnnamedTacticRuleActions()
    {
        var actions = ProtoEditorWindow.ParseTacticsActions("""
            <tactics>
              <action><name>Valid</name><type>Attack</type></action>
              <tactic><action>Valid</action><action>Move</action></tactic>
            </tactics>
            """);

        Assert.Single(actions);
        Assert.Contains("Valid", actions.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TacticsMigration_TypeChangeMaterializesOldEffectiveAction()
    {
        var oldBase = new ProtoAction { Name = "Shared", Type = "Attack", MaxRange = "10" };
        oldBase.AdditionalElements.Add(XElement.Parse("<persistent>1</persistent>"));
        var newBase = new ProtoAction { Name = "Shared", Type = "Heal", MaxRange = "20" };
        var overlay = new ProtoAction { Name = "Shared", Rof = "2" };

        var migrated = ProtoEditorWindow.BuildMigratedProtoActionsForTacticsChange(
            [overlay],
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = oldBase },
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = newBase });

        var action = Assert.Single(migrated);
        Assert.Equal("Attack", action.Type);
        Assert.Equal("10", action.MaxRange);
        Assert.Equal("2", action.Rof);
        Assert.Equal("1", Assert.Single(action.AdditionalElements).Value);
    }

    [Fact]
    public void TacticsMigration_SameTypeKeepsSparseOverlay()
    {
        var oldBase = new ProtoAction { Name = "Shared", Type = "Attack", MaxRange = "10" };
        var newBase = new ProtoAction { Name = "Shared", Type = "attack", MaxRange = "20" };
        var overlay = new ProtoAction { Name = "Shared", Rof = "2" };

        var migrated = ProtoEditorWindow.BuildMigratedProtoActionsForTacticsChange(
            [overlay],
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = oldBase },
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = newBase });

        Assert.Same(overlay, Assert.Single(migrated));
        Assert.Equal("", overlay.Type);
        Assert.Equal("", overlay.MaxRange);
    }

    [Fact]
    public void TacticsMigration_CompletelyInheritedTypeChangePreservesOldAction()
    {
        var oldBase = new ProtoAction { Name = "Shared", Type = "Attack", MaxRange = "10" };
        var newBase = new ProtoAction { Name = "Shared", Type = "Heal", MaxRange = "20" };

        var migrated = ProtoEditorWindow.BuildMigratedProtoActionsForTacticsChange(
            [],
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = oldBase },
            new Dictionary<string, ProtoAction>(StringComparer.OrdinalIgnoreCase) { ["Shared"] = newBase });

        var action = Assert.Single(migrated);
        Assert.Equal("Shared", action.Name);
        Assert.Equal("Attack", action.Type);
        Assert.Equal("10", action.MaxRange);
    }

    [Fact]
    public void TacticsPathResolution_RejectsTraversalAndAcceptsContainedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "aom-tactics-root");

        Assert.Null(ProtoEditorWindow.ResolveContainedTacticsPath(root, Path.Combine("..", "outside.tactics")));
        var accepted = ProtoEditorWindow.ResolveContainedTacticsPath(root, Path.Combine("sub", "inside.tactics"));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "sub", "inside.tactics")), accepted);
    }

    [Fact]
    public void InheritedStructuredProtection_CoversTrueDropdownsAndExplainsRemoval()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.Contains(
            "ComboBox comboBox => comboBox.SelectedItem?.ToString()?.Trim() ?? \"\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "GuardInheritedStructuredRowRemoval(removeButton, structuredTag, renderedType);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "BlockInheritedProtoActionRemovalAsync(\n                                    state,\n                                    [resourceSubTypeTag]",
            source.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void InheritedStructuredProtection_MatchesTrueDropdownRateKeysCaseInsensitively()
    {
        var tacticsAction = new ProtoAction();
        tacticsAction.AdditionalElements.Add(XElement.Parse("<rate type=\"Wood\">1</rate>"));

        Assert.True(ProtoEditorWindow.IsStructuredProtoActionEntryDefined(
            tacticsAction,
            "rate",
            "type",
            "wood"));
        Assert.False(ProtoEditorWindow.IsStructuredProtoActionEntryDefined(
            tacticsAction,
            "rate",
            "type",
            "Food"));
    }

    [Fact]
    public void InheritedStructuredProtection_CustomResourceDropdownsLockKeysAndGuardRemoval()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));

        Assert.True(
            source.Split("IsEnabled = !_isReadOnly && !isInheritedRateType", StringSplitOptions.None).Length - 1 >= 2,
            "Both ModifyGather and AutoGather resource dropdowns must lock tactics-owned rate keys.");
        Assert.True(
            source.Split("BlockInheritedStructuredProtoActionEntryRemovalAsync(", StringSplitOptions.None).Length - 1 >= 3,
            "The shared keyed guard must be used by both custom resource-rate removal handlers.");
    }

    private static List<XElement> Elements(params string[] xml)
        => xml.Select(XElement.Parse).ToList();
}
