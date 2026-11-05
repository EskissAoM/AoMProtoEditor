using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class PowerEditorView
{
    // Audited fixed XAML schema: these are numeric fields regardless of their
    // current text. New free-text fields must not become numeric by guessing.
    private static readonly HashSet<string> FixedPowerNumericFields = new(StringComparer.Ordinal)
    {
        "_activeTimeBox", "_buildUpBox", "_shutDownBox", "_shutDownFadeBox",
        "_costBox", "_repeatCostBox", "_limitGlobalBox", "_limitPlayerBox",
        "_restrictDistanceBox", "_indicatorRangeBox", "_indicatorWidthBox", "_indicatorSpeedBox",
        "_rangeIndicatorProtoRadiusBox", "_rangeIndicatorProtoCountBox", "_rangeIndicatorProtoSpeedBox", "_radiusBox",
        "_minimapDurationBox", "_cameraDurationBox", "_cameraStrengthBox", "_cameraRangeBox",
        "_terrainHeightBox", "_terrainWidthBox", "_terrainSpeedBox", "_terrainFadeTimeBox",
        "_dualPowerMinDistanceBox", "_powerBlockerBox", "_revealRadiusBox", "_accuracyBox",
        "_allyDamageRatioBox", "_crackIntervalMinBox", "_crackIntervalMaxBox", "_timeFactorBox",
        "_stunLengthBox", "_minThrowDistanceBox", "_maxThrowDistanceBox", "_minThrowHeightBox",
        "_maxThrowHeightBox", "_minThrowVelocityBox", "_maxThrowVelocityBox", "_throwSizeLimitBox",
        "_treeAmountBox", "_treeSpawnIntervalMinBox", "_treeSpawnIntervalMaxBox", "_growthRadiusStartBox",
        "_growthRadiusIncreaseRateBox", "_damagePercentConversionBox", "_unitEffectTimeSecondsBox", "_sharedEffectTimeSecondsBox",
        "_spiderAttackRangeBox", "_spiderSpawnOffsetBox", "_numberToCreateBox", "_separationDistanceBox",
        "_hatchTimeBox", "_hatchIntervalMinBox", "_hatchIntervalMaxBox", "_warnTimeBox",
        "_initialDelayBox", "_unitEffectTimeBox", "_dropIntervalMinBox", "_dropIntervalMaxBox",
        "_maxRetryBox", "_maxUnitHeightBox", "_maxSpawnNumberBox", "_sinkHoleTimeBox",
        "_sinkHoleShakeBox", "_rumbleTimeBox", "_firstSpawnDelayBox", "_spawnIntervalMinBox",
        "_spawnIntervalMaxBox", "_implodePullRadiusBox", "_implodeEscapeRadiusBox", "_implodeStrengthIncrementBox",
        "_implodeMaxStrengthBox", "_implodeOverkillIncrementBox", "_implodeStrengthAdjustBox", "_implodeUnitAccelerationBox",
        "_implodeWindupLengthBox", "_implodeExplosionRadiusBox", "_implodeAccumulationBox", "_implodeMaxAccumulatedBox",
        "_implodeExplosionDelayBox", "_implodeShockwaveVelocityBox", "_implodeSphereScaleBox", "_implodeSphereHeightBox",
        "_implodeHeightAdjustBox", "_implodeMovementDelayBox", "_implodeFloatAdjustBox", "_implodeDamageIntervalBox",
        "_implodeBaseShearIncrementBox", "_implodeShearMultiplierBox", "_implodeBaseShearMaxBox", "_implodeDelayMinBox",
        "_implodeDelayMaxBox", "_implodeEndDelayMinBox", "_implodeEndDelayMaxBox", "_implodeMinStartingBox",
        "_implodeMinPullingBox", "_implodeMinExplodingBox", "_implodeInnerRadiusBox", "_implodeMiddleRadiusBox",
        "_implodeOuterRadiusBox", "_implodeInnerAngularBox", "_implodeMiddleAngularBox", "_implodeOuterAngularBox",
        "_implodeInitialShakeDurationBox", "_implodeInitialShakeStrengthBox", "_implodeDeathShakeDurationBox", "_implodeDeathShakeStrengthBox",
        "_implodeExplosionShakeDurationBox", "_implodeExplosionShakeStrengthBox", "_numberToTransformBox", "_wallSegmentCountBox",
        "_stayTimeBox", "_rootDurationBox", "_restorationHealSpeedBox", "_slowHealMultiplierBox",
        "_restorationScanIntervalBox", "_vibrantGenerateIntervalBox", "_vibrantRotateMinBox", "_vibrantRotateMaxBox",
        "_vibrantGrowthMinBox", "_vibrantGrowthMaxBox", "_vibrantScaleMinBox", "_vibrantScaleMaxBox",
        "_droughtBurnTimeBox", "_greatFloodWaveCountBox", "_greatFloodWaveRadiusBox", "_greatFloodSpacingBox",
        "_greatFloodInitialVelocityBox", "_greatFloodFinalVelocityBox", "_greatFloodRampTimeBox", "_greatFloodAttackIntervalBox",
        "_shiftingSandsFadeTimeBox", "_shiftingSandsSpawnTimeBox", "_shiftingSandsScanIntervalBox", "_shiftingSandsPlacementRadiusBox",
        "_locustSwarmCountBox", "_locustSwarmRadiusBox", "_locustWayPointCountBox", "_locustWayPointOffsetBox",
        "_locustDamageIntervalBox", "_boltStrikeHeightBox", "_boltStrikeMinOffsetBox", "_boltStrikeMaxOffsetBox",
        "_pestilenceRofPenaltyBox", "_pestilenceTrainRatePenaltyBox", "_pestilenceScanIntervalBox", "_greatHuntFoodRatioBox",
        "_stampedeScaleBox", "_stampedeAnimRateBox", "_stampedeAttackIntervalBox", "_swapMinAmountBox",
        "_swapMaxAmountBox", "_curseMaxResourcesBox", "_curseConvertRatioBox", "_curseSwapDelayBox",
        "_gustLengthBox", "_gustSpeedBox", "_gustMinFactorBox", "_gustMaxFactorBox",
        "_gustBlowTimeBox", "_gustTimeBox", "_gustThrowSizeBox", "_divineAttackDelayBox",
        "_divineDamageWidthBox", "_divineDamageDepthBox", "_divineCameraDelayBox", "_lureMinRangeBox",
        "_lureMaxRangeBox", "_lureAttractRateBox", "_lureVarianceBox", "_lureCapDistanceBox",
        "_undermineDamageRadiusBox", "_undermineAttackIntervalBox", "_undermineDeformBox", "_forestFireBurnLengthBox",
        "_forestFireFadeLengthBox", "_forestFireEmbersLifespanBox", "_forestFireTreeDestructionBox", "_forestFireTreeSearchRadiusBox",
        "_forestFireDamageSearchRadiusBox", "_tempestSlowestStrikeBox", "_tempestFastestStrikeBox", "_tempestStrikeHeightBox",
        "_tempestStrikeMinOffsetBox", "_tempestStrikeMaxOffsetBox", "_tempestCloudBuildStartBox", "_tempestCloudBuildEndBox",
        "_tempestCloudFadeStartBox", "_tempestCloudFadeEndBox", "_tempestSlowestDriftBox", "_tempestFastestDriftBox",
        "_tempestStrengthMinBox", "_tempestStrengthMaxBox", "_tempestPeriodMinBox", "_tempestPeriodMaxBox",
        "_tempestAmplitudeMinBox", "_tempestAmplitudeMaxBox", "_tempestAngleOffsetBox", "_tempestTreeDurationBox",
        "_lightningRainBuildStartBox", "_lightningRainBuildEndBox", "_lightningRainFadeStartBox", "_lightningRainFadeEndBox",
        "_earthquakeIntervalBox", "_earthquakeTreeAccuracyBox", "_earthquakeEpicenterRadiusBox", "_earthquakeMiddleRadiusBox",
        "_earthquakeOuterRadiusBox", "_earthquakeEpicenterDamageBox", "_earthquakeMiddleDamageBox", "_earthquakeOuterDamageBox",
        "_thunderBurstAmountBox", "_thunderBurstDelayBox", "_thunderBurstRadiusIncreaseBox", "_thunderBurstStunDurationBox",
        "_typhoonDragonSpawnDelayBox", "_typhoonDragonAttackIntervalBox", "_typhoonSpawnDelayBox", "_typhoonAttackDelayBox",
        "_typhoonAttackIntervalBox", "_typhoonRadiusStartBox", "_typhoonRadiusEndBox", "_typhoonRadiusExpansionBox",
        "_frostExpansionBox", "_frostTransitionBox", "_frostDurationBox", "_frostStrikeMinBox",
        "_frostStrikeMaxBox", "_frostArmorBox", "_frostSlowestDriftBox", "_frostFastestDriftBox",
        "_witherDurationBox", "_witherVfxIntervalBox", "_witherDamageBox", "_witherCreepGeneratorsBox",
        "_arcadianBlockerDurationBox", "_underworldEidolonMinBox", "_underworldEidolonMaxBox", "_underworldEidolonStepBox",
        "_underworldMaxHpMultiplierBox", "_underworldMaxSpeedMultiplierBox", "_underworldSpawnRadiusBox", "_underworldSpawnDurationBox",
        "_underworldLightBuildUpBox", "_underworldLightDurationBox", "_underworldLightShutdownBox", "_underworldSuddenDeathBox",
        "_effectInAreaRepeatDelayBox", "_switchingIntervalBox", "_newMoonMaxFreeBox", "_newMoonRateBox",
        "_fimbulWolfSoftCapBox", "_fimbulWolfHardCapBox", "_fimbulMainPackSizeBox", "_fimbulMainPackRadiusBox",
        "_fimbulExtraSpawnsBox", "_fimbulSecondaryPackSizeBox", "_fimbulSecondaryPackRadiusBox", "_fimbulSecondaryPackMaxRadiusBox",
        "_fimbulWanderDistanceBox", "_fimbulMaxWanderDistanceBox", "_fimbulScanIntervalBox", "_fimbulEvalRadiusBox",
        "_fimbulSearchRadiusBox", "_fimbulSlowestStrikeBox", "_fimbulFastestStrikeBox", "_cageFloatHeightBox",
        "_cageResourceCapBox", "_earthMonsterAdditionalStrikesBox", "_earthMonsterStrikeIntervalBox", "_earthMonsterUnitLimitBox",
        "_earthMonsterMinGroupBox", "_earthMonsterGroupRadiusBox", "_earthMonsterAvoidRadiusBox", "_earthMonsterRegurgitateTimingBox",
        "_earthMonsterRegurgitateDurationBox", "_earthMonsterRegurgitateHeightBox", "_earthMonsterRegurgitateAnimDurationBox", "_earthMonsterSwallowTimerBox",
        "_earthMonsterChompHeightBox", "_earthMonsterThrowUpwardBox", "_earthMonsterThrowOutwardBox", "_meteorStandardStrikesBox",
        "_meteorPriorityStrikesBox", "_meteorMinGroupSizeBox", "_meteorGroupSearchRadiusBox", "_meteorSecondaryAccuracyBox",
        "_tornadoDamageIntervalBox", "_tornadoGreaterDamageRadiusBox", "_tornadoWaypointCountBox", "_tornadoSpiralSizeBox",
        "_tornadoFunnelRadiusBox", "_tornadoFunnelHeightBox", "_tornadoDebrisXzBox", "_tornadoDebrisYBox",
        "_tornadoDebrisAngularBox",
    };

    private static readonly string[] FixedPowerSelectors =
    [
        "_placementFlagPicker", "_targetTypePicker", "_excludeTypePicker", "_losProtoUnitBox",
        "_restrictPlacementBox", "_overlapTypeBox", "_rangeIndicatorProtoBox", "_attackTargetTypePicker",
        "_attackExcludeTypePicker", "_revealUnitBox", "_lightingBox", "_resetAbilityChargeBox",
        "_otherAttributePicker", "_powerTypeBox", "_otherSpecificAttributePicker", "_cracksSoundBox",
        "_shockwaveVfxBox", "_throwExcludeThrowPicker", "_treeUnitBox", "_treeEffectVfxBox",
        "_effectVfxBox", "_vfxProtoBox", "_spiderUnitBox", "_spiderEggUnitBox",
        "_spiderDoorBox", "_spiderWarnBox", "_humanDeathSoundBox", "_vortexLandVfxBox",
        "_vortexStartVfxBox", "_vortexStartLinkedBox", "_vortexFinishLinkedBox", "_liftSoundBox",
        "_landSoundBox", "_spawnProtoBox", "_tartarianRevealerBox", "_tartarianGateUnitBox",
        "_tartarianGateBirthBox", "_tartarianGateFlameBox", "_implodeSphereBox", "_implodeShockwaveBox",
        "_implodePullEffectBox", "_implodeStartSoundBox", "_implodeExplodeSoundBox", "_implodeWindupSoundBox",
        "_implodeHitSoundBox", "_farmProtoBox", "_pSeedsFarmVfxBox", "_pSeedsFarmTargetVfxBox",
        "_wallProtoBox", "_vanishCastVfxBox", "_vanishTargetVfxBox", "_reappearVfxBox",
        "_rootedEnemyVfxBox", "_treeAreaVfxBox", "_forestTreeVfxBox", "_vibrantTreeVfxBox",
        "_restorationHealVfxBox", "_restorationAreaVfxBox", "_droughtLandVfxBox", "_droughtWaveUnitBox",
        "_greatFloodWaveUnitBox", "_greatFloodAttackNameBox", "_shiftingSandsStartPortalBox", "_shiftingSandsDestinationPortalBox",
        "_locustSwarmUnitBox", "_boltStrikeUnitBox", "_boltSparksVfxBox", "_boltScorchVfxBox",
        "_pestilenceSmallVfxBox", "_pestilenceLargeVfxBox", "_teleportStartPortalBox", "_teleportDestinationPortalBox",
        "_greatHuntSpawnIfNoTargetBox", "_greatHuntVfxBox", "_spyIndicatorBox", "_stampedeUnitBox",
        "_stampedeAttackActionBox", "_swapFlagPicker", "_curseVfxBox", "_gustUnitBox",
        "_gustDamagingUnitBox", "_gustDamageActionBox", "_divineAttackUnitBox", "_divineAttackActionBox",
        "_divineHitDecalBox", "_lureUnitBox", "_lureAttractTypePicker", "_lureSpawnProtoBox",
        "_lureSpawnVfxBox", "_undermineAttackUnitBox", "_forestFireAttackUnitBox", "_forestFireEmbersUnitBox",
        "_forestFireVfxBox", "_tempestStrikeUnitBox", "_tempestStrikeSoundBox", "_tempestLargeStrikeUnitBox",
        "_tempestAvoidPicker", "_tempestLargeSoundBox", "_tempestAreaAttackActionBox", "_tempestEffectBox",
        "_tempestGroundBox", "_tempestImpactBox", "_tempestIceBox", "_lightningSparksBox",
        "_lightningGroundSparksBox", "_lightningScorchBox", "_lightningRainVfxBox", "_lightningCloudVfxBox",
        "_earthquakeAttackUnitBox", "_earthquakeDustSmallBox", "_earthquakeDustMediumBox", "_earthquakeDustLargeBox",
        "_thunderBurstStartingVfxBox", "_thunderBurstChargingVfxBox", "_thunderBurstBurstVfxBox", "_typhoonDragonUnitBox",
        "_typhoonUnitBox", "_typhoonRainUnitBox", "_frostStormBox", "_frostSnowDriftBox",
        "_witherWaveUnitBox", "_witherKillTypePicker", "_arcadianPlacementBox", "_arcadianDamageReceiverBox",
        "_arcadianBlockerBox", "_arcadianVfxBox", "_underworldEidolonTypeBox", "_underworldStartVfxBox",
        "_underworldEidolonSpawnVfxBox", "_underworldNoBirthVfxBox", "_underworldEffectVfxBox", "_underworldLightSetBox",
        "_underworldTechEffectBox", "_underworldOnlyIfTechBox", "_powerClonePlacementVfxBox", "_powerCloneReplacementPicker",
        "_ceaseFireVfxBox", "_ceaseFireAttachBox", "_prosperityLargeVfxBox", "_prosperitySmallVfxBox",
        "_prosperityTypePicker", "_effectInAreaAreaVfxBox", "_rainLargeTargetBox", "_rainLargeVfxBox",
        "_rainSmallTargetBox", "_rainSmallVfxBox", "_switchingVfxAttachPicker", "_modifyVfxTargetBox",
        "_modifyVfxBoneBox", "_modifyVfxBirthBox", "_foxCharmUnitVfxBox", "_foxCharmTargetVfxBox",
        "_foxCharmAreaVfxBox", "_fimbulWolfPicker", "_fimbulWolfVfxBox", "_fimbulMainPackTargetBox",
        "_fimbulSecondaryPackTargetBox", "_cageSimilarTypePicker", "_cageVfxAreaBox", "_cageVfxMainTargetBox",
        "_cageVfxMainTargetBoneBox", "_cageVfxMainImpactBox", "_cageVfxMarkAnimBox", "_cageVfxMarkAnimBoneBox",
        "_cageVfxAdditionalImpactBox", "_cageAdditionalImpactSoundBox", "_earthMonsterUnitBox", "_earthMonsterStrikeActionBox",
        "_earthMonsterCrushActionBox", "_earthMonsterRegurgitateAnimBox", "_earthMonsterLandImpactBox", "_earthMonsterEmergeSoundBox",
        "_earthMonsterChompSoundBox", "_earthMonsterRegurgitateSoundBox", "_meteorUnitBox", "_meteorLandVfxBox",
        "_meteorWaterVfxBox", "_meteorFirstApproachSoundBox", "_meteorFirstStrikeSoundBox", "_meteorStrikeSoundBox",
        "_meteorApproachSoundBox", "_tornadoUnitBox", "_throwExcludePullPicker",
    ];

    private static readonly string[] FixedPowerEnums =
    [
        "_placementBox", "_rangeIndicatorBox", "_powerPlayerRelationBox", "_alertPlayerBox",
        "_minimapSendToBox", "_activeListVisibilityBox", "_stealthLevelBox", "_powerCloneSlotBox",
        "_switchingTierTypeBox", "_switchingProgressTypeBox", "_earthMonsterSwallowRelationBox",
    ];

    private void ConfigureFixedPowerFieldPresentation()
    {
        // Some fixed rows are deliberately detached from the logical tree until
        // their Power type/optional section is selected. Resolve their registered
        // XAML names, so hidden rows receive the same role before first opening.
        var fields = FixedPowerNumericFields.Concat(FixedPowerSelectors).Concat(FixedPowerEnums)
            .Concat(["_nameBox", "_displayNameBox", "_orderHintBox", "_rolloverBox", "_activeRolloverBox",
                "_exclusiveMessageBox", "_rangeDecalBox", "_castMessageBox", "_castSelfMessageBox",
                "_switchingTimerRolloverBox", "_iconEditor"])
            .Select(name => this.FindControl<Control>(name) ?? throw new InvalidOperationException($"Missing Power field {name}."))
            .ToArray();
        foreach (var field in fields)
        {
            // Long bodies have the shared responsive rollover configuration.
            if (field is TextBox { AcceptsReturn: true }) continue;
            var role = field switch
            {
                TextBox box when FixedPowerNumericFields.Contains(box.Name ?? "") => EditorFieldWidthRole.Numeric,
                TextBox => EditorFieldWidthRole.SingleLineText,
                AutoCompleteBox => EditorFieldWidthRole.Reference,
                ComboBox => EditorFieldWidthRole.ShortEnum,
                _ => EditorFieldWidthRole.AssetPath
            };
            ConfigurePowerFieldPresentation(field, role);
        }
        ConfigurePowerFieldPresentation(_orderHintBox, EditorFieldWidthRole.DenseNumeric);
        ConfigurePowerFieldPresentation(_exclusiveMessageBox, EditorFieldWidthRole.ShortRollover);
        ConfigurePowerFieldPresentation(_rangeDecalBox, EditorFieldWidthRole.AssetPath);

        foreach (var picker in new[] { _placementFlagPicker, _targetTypePicker, _excludeTypePicker,
                     _attackTargetTypePicker, _attackExcludeTypePicker, _throwExcludeThrowPicker,
                     _swapFlagPicker, _lureAttractTypePicker, _tempestAvoidPicker, _witherKillTypePicker,
                     _powerCloneReplacementPicker, _prosperityTypePicker, _switchingVfxAttachPicker,
                     _fimbulWolfPicker, _cageSimilarTypePicker, _throwExcludePullPicker })
            ConfigurePowerFieldPresentation(picker, EditorFieldWidthRole.ChipSearch);

        foreach (var picker in new[] { _otherAttributePicker, _otherSpecificAttributePicker, _powerTypeBox })
            ConfigurePowerFieldPresentation(picker, EditorFieldWidthRole.LongEnum);
        foreach (var picker in new[] { _powerPlayerRelationBox, _alertPlayerBox, _minimapSendToBox,
                     _activeListVisibilityBox, _switchingTierTypeBox, _switchingProgressTypeBox,
                     _earthMonsterSwallowRelationBox })
            ConfigurePowerFieldPresentation(picker, EditorFieldWidthRole.LongEnum);

        foreach (var picker in ImplodeSoundBoxes().Concat(MeteorSoundBoxes()).Concat(new[]
                 { _cracksSoundBox, _humanDeathSoundBox, _liftSoundBox, _landSoundBox,
                   _tempestStrikeSoundBox, _tempestLargeSoundBox, _cageAdditionalImpactSoundBox,
                   _earthMonsterEmergeSoundBox, _earthMonsterChompSoundBox, _earthMonsterRegurgitateSoundBox }))
            ConfigurePowerFieldPresentation(picker, EditorFieldWidthRole.AssetPath);
    }

    private T ConfigurePowerFieldPresentation<T>(T field, EditorFieldWidthRole role) where T : Control
    {
        EditorFormLayout.ApplyWidth(field, role);
        EditorFieldInteraction.Configure(field, role, () => _isPopulating);
        if (field is AutoCompleteBox or ComboBox || field is TextBox { AcceptsReturn: false })
            field.MinHeight = field.Height = field.MaxHeight = EditorFormLayout.FieldHeight;
        if (field is AutoCompleteBox selector)
            EditorAutoCompleteService.KeepDropDownAnchored(selector);
        return field;
    }

    private static EditorFieldWidthRole PowerOnHitCatalogRole(string attribute)
        => attribute.ToLowerInvariant() switch
        {
            "proto" or "attachbone" or "protoaction" or "targetunittype" or "byunittype" or "anim"
                => EditorFieldWidthRole.Reference,
            "freezetype" => EditorFieldWidthRole.LongEnum,
            _ => EditorFieldWidthRole.ShortEnum
        };

    private static bool PowerInput(Control field)
        => field is TextBox or AutoCompleteBox or ComboBox or AssetPathEditor;

    private static bool PowerCross(Control field)
        => field is Button button && button.Classes.Contains("remove-button") && !button.Classes.Contains("remove-card");

    private void FinalizePowerPresentation()
    {
        if (_currentEntry != null)
        {
            foreach (var name in FixedPowerNumericFields.Concat(FixedPowerSelectors).Concat(FixedPowerEnums))
                if (this.FindControl<Control>(name) is { Tag: string tag } field)
                    EditorValidationBindings.Bind(field, _currentEntry, tag);
            EditorValidationBindings.Bind(_nameBox, _currentEntry, "@name");
            EditorValidationBindings.Bind(_powerTypeBox, _currentEntry, "@type");
            EditorValidationBindings.Bind(_orderHintBox, _currentEntry, "@orderhint");
        }
        // Explicit lifecycle boundary, not a LayoutUpdated observer. Rebuilt rows
        // are picked up after population/edits and selector-driven refreshes.
        var controls = _mainGrid.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
        foreach (var card in controls.OfType<Border>().Where(border => border.Classes.Contains("temp-unit-card")))
            if (card.Child is Grid grid)
                foreach (var remove in grid.Children.OfType<Button>().Where(button => button.Classes.Contains("remove-button")))
                {
                    remove.Classes.Remove("remove-field"); remove.Classes.Add("remove-card");
                    remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top;
                }
        foreach (var row in controls.OfType<WrapPanel>().Reverse()) GroupPowerFlowRow(row);
        foreach (var grid in controls.OfType<Grid>()) EditorFormLayout.NormalizeFieldGrid(grid);
        foreach (var row in controls.OfType<StackPanel>().Where(row => row.Orientation == Orientation.Horizontal && row.Children.Any(PowerInput)))
            EditorFormLayout.UseInlineRowLayout(row);
        foreach (var (name, row, _) in OtherAttributeSpecs())
        {
            var remove = _removeOtherAttributeButtons[name];
            var content = row.Children.FirstOrDefault(child => Grid.GetColumn(child) == 1);
            var anchor = content is Control control && PowerInput(control) ? control :
                content?.GetLogicalDescendants().OfType<Control>().FirstOrDefault(PowerInput);
            var compound = name is "Notification" or "Minimap Notification" or "Active Power Visibility" or "Sound set" or "Attack target";
            if (!compound && anchor?.Parent is StackPanel { Orientation: Orientation.Horizontal } group)
            {
                if (remove.Parent is Panel parent && !ReferenceEquals(parent, group)) parent.Children.Remove(remove);
                if (!group.Children.Contains(remove)) group.Children.Insert(group.Children.IndexOf(anchor) + 1, remove);
                EditorFormLayout.UseInlineRowLayout(group);
            }
            else if (compound)
            {
                remove.Classes.Add("remove-card");
                remove.HorizontalAlignment = HorizontalAlignment.Right;
                remove.VerticalAlignment = VerticalAlignment.Top;
                remove.Margin = new Thickness(EditorFormLayout.LabelGap, 0, 0, 0);
            }
        }
    }

    private static void GroupPowerFlowRow(WrapPanel row)
        => EditorFormLayout.GroupFlowingFields(row);
}
