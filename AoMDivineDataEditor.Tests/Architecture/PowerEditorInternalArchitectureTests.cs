using Xunit;

namespace AoMDivineDataEditor.Tests;

[Trait("Category", TestCategories.Architecture)]
public sealed class PowerEditorInternalArchitectureTests
{
    [Fact]
    public void SaveCoreDelegatesUiIndependentPreparationToTheDomainService()
    {
        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");
        var saveCore = ProjectSource.ExtractMethod(view, "private async Task<bool> SaveCoreAsync");

        Assert.Contains("PowerSavePreparation.Prepare", saveCore, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeNestedElementIndentation", saveCore, StringComparison.Ordinal);
        Assert.DoesNotContain("FindUnrecognizedReferences", saveCore, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameReferences", saveCore, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerTypesAndSpecificGroupLayoutsHaveOneRegistrySource()
    {
        Assert.Equal(63, Classes.PowerSchemaRegistry.OriginalPowerTypes.Count);
        Assert.Equal(["FoxCharm"], Classes.PowerSchemaRegistry.ExtensionPowerTypes);
        Assert.Equal(
            Classes.PowerSchemaRegistry.EditorPowerTypes.Count,
            Classes.PowerSchemaRegistry.Schemas.Count);
        Assert.All(Classes.PowerSchemaRegistry.Schemas.Values, schema =>
            Assert.Equal(
                schema.SpecificGroups.Count,
                schema.SpecificGroups.Distinct(StringComparer.OrdinalIgnoreCase).Count()));

        var allGroups = Classes.PowerSchemaRegistry.Schemas.Values
            .SelectMany(schema => schema.SpecificGroups)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Subset(allGroups, Classes.PowerSchemaRegistry.TypeOnlySpecificGroups.ToHashSet(StringComparer.OrdinalIgnoreCase));

        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");
        var specs = ProjectSource.ExtractBetween(
            view,
            "private (string Name, Grid Row, string[] Tags)[] SpecificAttributeSpecs()",
            "private static bool IsDefaultSpecificAttribute");
        foreach (var group in allGroups)
            Assert.Contains($"(\"{group}\",", specs, StringComparison.Ordinal);
        Assert.DoesNotContain("SpecificAttributeLayouts", view, StringComparison.Ordinal);
        Assert.DoesNotContain("NonAdditionalSpecificAttributes", view, StringComparison.Ordinal);

        var audit = ProjectSource.Read("Classes", "PowerEditorAuditInventory.cs");
        Assert.Contains("PowerSchemaRegistry.OriginalPowerTypes", audit, StringComparison.Ordinal);
        Assert.Contains("PowerSchemaRegistry.ExtensionPowerTypes", audit, StringComparison.Ordinal);
        Assert.Contains("PowerSchemaRegistry.EditorPowerTypes", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void ComplexPowerFamiliesDelegateDomainRulesToFocusedControllers()
    {
        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");
        foreach (var controller in new[]
                 {
                     "PowerEffectFamilyController", "EffectInAreaFamilyController",
                     "UnitModifyFamilyController", "SwitchingEffectsFamilyController"
                 })
            Assert.Contains(controller, view, StringComparison.Ordinal);

        Assert.DoesNotContain("STR_SWITCHING_EFFECT_", view, StringComparison.Ordinal);
        var removeModifyVfx = ProjectSource.ExtractMethod(
            view, "private void RemoveModifyVfx_Click");
        Assert.Contains("UnitModifyFamilyController.AllVfxTags", removeModifyVfx,
            StringComparison.Ordinal);

        var tierHandler = ProjectSource.ExtractMethod(
            view, "private void SwitchingTierType_SelectionChanged");
        Assert.Contains("SwitchingEffectsFamilyController.ApplyTierType", tierHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("resourcetierreq", tierHandler, StringComparison.Ordinal);

        var effectDefaults = ProjectSource.ExtractMethod(
            view, "private static void InitializeRainEffectForType");
        Assert.Contains("PowerEffectFamilyController.InitializeForType", effectDefaults,
            StringComparison.Ordinal);
        Assert.DoesNotContain("armorvulnerability", effectDefaults, StringComparison.OrdinalIgnoreCase);

        var effectCard = ProjectSource.ExtractMethod(view, "private void BuildRainEffectCard");
        Assert.Contains("PowerEffectFamilyController.ChangeType", effectCard, StringComparison.Ordinal);
        Assert.DoesNotContain("effect.RemoveNodes()", effectCard, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicPowerEditorsUsePowerSpecificVisualPrimitives()
    {
        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");

        foreach (var methodSignature in new[]
                 {
                     "private void BuildSwitchingEffectContainer",
                     "private void BuildUnitModifyCard",
                     "private void BuildRainEffectCard",
                     "private void BuildTempUnitCreateRow"
                 })
        {
            var method = ProjectSource.ExtractMethod(view, methodSignature);
            Assert.True(
                method.Contains("PowerEditorDynamicControls.CreateCard", StringComparison.Ordinal) ||
                method.Contains("PowerEditorDynamicControls.CreateCardSurface", StringComparison.Ordinal),
                $"{methodSignature} must use the Power card primitive.");
        }

        Assert.Contains(
            "PowerEditorDynamicControls.CreateStrictSelector",
            ProjectSource.ExtractMethod(view, "private AutoCompleteBox CreateRainStrictSelector"),
            StringComparison.Ordinal);
        Assert.Contains(
            "PowerEditorDynamicControls.CreateLabeledSegment",
            ProjectSource.ExtractMethod(view, "private static StackPanel RainLabeledSegment"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("EditorChipService.CreateBlueChip", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes.Add(\"temp-unit-card\")", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes.Add(\"remove-button\")", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes = { \"remove-button\" }", view, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerEditingStateHasOneSessionBoundary()
    {
        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");

        Assert.Contains("private readonly PowerEditingSession _session", view, StringComparison.Ordinal);
        Assert.Contains("_session.CreateCommitSnapshot()", view, StringComparison.Ordinal);
        Assert.Contains("_session.AcceptCommit()", view, StringComparison.Ordinal);
        Assert.Contains("_session.DiscardPowerState", view, StringComparison.Ordinal);
        Assert.Contains("_session.FindConflictingRename", view, StringComparison.Ordinal);
        Assert.Contains("_session.Transient", view, StringComparison.Ordinal);

        foreach (var removedParallelField in new[]
                 {
                     "Dictionary<string, string> _pendingStringUpdates",
                     "Dictionary<string, string> _pendingStringOwners",
                     "HashSet<string> _pendingStringRemovals",
                     "Dictionary<string, string> _pendingStringRemovalOwners",
                     "HashSet<string> _dirtyNames",
                     "List<KeyValuePair<string, string>> _pendingRenames",
                     "private XElement? _currentEntry;",
                     "private string? _loadedEntryName;"
                 })
            Assert.DoesNotContain(removedParallelField, view, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCompositionUsesOneExplicitContextBoundary()
    {
        var view = ProjectSource.Read("Windows", "PowerEditorView.axaml.cs");
        var host = ProjectSource.Read("Windows", "ProtoEditorWindow.axaml.cs");
        var publicConstructor = ProjectSource.ExtractMethod(
            view, "public PowerEditorView(PowerEditorComposition composition)");

        Assert.Contains("composition.BaseDefinitions", publicConstructor, StringComparison.Ordinal);
        Assert.Contains("composition.Catalogs", publicConstructor, StringComparison.Ordinal);
        Assert.Contains("composition.HostServices", publicConstructor, StringComparison.Ordinal);
        Assert.Contains("internal PowerEditorView(", view, StringComparison.Ordinal);
        Assert.DoesNotContain("public PowerEditorView(\n        IEnumerable<PowerDefinition>", view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly Func<", view, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly IReadOnlyList<string>", view, StringComparison.Ordinal);

        Assert.Contains("new PowerEditorCatalogs(", host, StringComparison.Ordinal);
        Assert.Contains("new PowerEditorHostServices(", host, StringComparison.Ordinal);
        Assert.Contains("new PowerEditorView(new PowerEditorComposition(", host, StringComparison.Ordinal);
    }
}
