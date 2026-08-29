using System;
using System.IO;
using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionSharedLayoutPolishRegressionTests
{
    [Fact]
    public void Attaching_RendersSingleUseAfterMaxRangeWithoutGenericDuplicate()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Tag = \"attaching.singleuse\"", source, StringComparison.Ordinal);
        Assert.Contains("var singleUseColumn = Grid.GetColumn(state.MaxRangeTb) + 1;", source, StringComparison.Ordinal);
        Assert.Contains("IsManagedAttachingFieldTag(actionType, normalized)", source, StringComparison.Ordinal);
        Assert.Contains("RegisterProtoActionFlagSourceControl(state, singleUseCheckBox, \"singleuse\")", source, StringComparison.Ordinal);
        Assert.Contains("SyncFlagCheckboxesFromFlags();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoConvert_UsesOneCompactConvertedByRow()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Text = \"Can be converted by:\"", source, StringComparison.Ordinal);
        Assert.Contains("var checkboxRow = convertPanel;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Can Convert Unit From", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachmentTrio_UsesSharedWidthsLabelsAndInlineTimer()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("tag.Equals(\"modelattachment\"", source, StringComparison.Ordinal);
        foreach (var boneTag in new[] { "modelattachmentbone", "targetattachmentbone", "infectionattachmentbone" })
            Assert.Contains($"tag.Equals(\"{boneTag}\"", source, StringComparison.Ordinal);
        Assert.Contains("CreateProtoActionModelAttachmentEditors(", source, StringComparison.Ordinal);
        Assert.Contains("GetProtoActionValueSuggestions(\"modelattachment\")", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = GetAvailableProtoActionModelAttachmentBones()", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Timer (ms):\", timerEditor)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Timer\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSelectors_PutInputBesideLabelAndChipsBelow()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("selectorRow.Children.Add(picker);", source, StringComparison.Ordinal);
        Assert.Contains("section.Children.Add(chips);", source, StringComparison.Ordinal);
        Assert.Contains("flagsHeaderRow.Children.Add(acbAdd);", source, StringComparison.Ordinal);
        Assert.Contains("state.FlagsContainer.Children.Add(flagsWrap);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoGather_UsesUniqueWrappedRatesAndNestedUnitTypeChips()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("var typeCombo = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("var ratesContainer = new WrapPanel", source, StringComparison.Ordinal);
        Assert.Contains("RefreshAutoGatherResourceChoices", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Rate\"", source, StringComparison.Ordinal);
        Assert.Contains("addAutoGatherRateButton.IsVisible = ProtoConstants.KnownResourceTypes.Any", source, StringComparison.Ordinal);
        Assert.Contains("ratesContainer.Children.Add(addAutoGatherRateButton);", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Additional Properties\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Scale by gather rate\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Do Not Auto Gather Unless Gathering Types\"", source, StringComparison.Ordinal);
        Assert.Contains("var gatheringTypeChips = new WrapPanel", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"donotautogatherunlessgatheringtypes\"", source, StringComparison.Ordinal);
        Assert.Contains("GetProtoActionNestedUnitTypeValues", source, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(option => ShouldShowAutoGatherOptionalField(option.Tag))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BolsterAndChargedModifiers_UseRequestedCompactControls()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.DoesNotContain("Content = \"Max stacks on target\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultVisibleTags: [\"modifyamount\", \"anim\", \"maxrange\", \"rate\", \"projectile\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("DefaultVisibleTags: [\"anim\", \"maxrange\", \"rate\", \"projectile\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("IsBolsterActionType(actionType)", source, StringComparison.Ordinal);
        Assert.Contains("x.Equals(\"modifyamount\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("GetVisibleProtoActionSimpleFieldTags(state, effectiveAction, actionType)", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Animation:\", animEditor", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Projectile:\", projectileEditor", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Modify:\"", source, StringComparison.Ordinal);
        Assert.Contains("var paramCb = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("\"ArmorSpecific\" => [\"Hack\", \"Pierce\", \"Crush\"]", source, StringComparison.Ordinal);
        Assert.Contains("\"DamageSpecific\" => [\"Hack\", \"Pierce\", \"Crush\", \"Divine\"]", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionDecimalBehavior(valueTb, () => true)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Charged Modifier\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuckAttackAndBuild_UseSpecializedOptionalAndPostRateLayouts()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Tag = \"buckattack.linearshockwave\"", source, StringComparison.Ordinal);
        Assert.Contains("linearShockwaveSpacerColumn", source, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions[maxRangeColumn].Width = new GridLength(90)", source, StringComparison.Ordinal);
        Assert.Contains("Width = new GridLength(8)", source, StringComparison.Ordinal);
        Assert.Contains("IsBuckAttackActionType(actionType) ||", source, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalButton(\"Add Animation by Type\", \"typedanim\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalButton(\"Add Max Range by Type\", \"typedmaxrange\")", source, StringComparison.Ordinal);
        Assert.Contains("state.BuckAttackOptionsContainer.IsVisible =", source, StringComparison.Ordinal);
        Assert.Contains("? \"Animations by Type\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"Max Range by Type\"", source, StringComparison.Ordinal);
        Assert.Contains("container.Children.Insert(insertIndex++, state.BuckAttackOptionsContainer);", source, StringComparison.Ordinal);
        Assert.Contains("IsActionCardManagedOptionalFieldTag(actionType, x.Tag)", source, StringComparison.Ordinal);
        Assert.Contains("IsActionCardManagedOptionalFieldTag(actionType, x)", source, StringComparison.Ordinal);
        Assert.Contains("? \"Default animation\"", source, StringComparison.Ordinal);
        Assert.Contains("(\"maxsizeclass\", \"Max Size Class\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"stunduration\", \"Stun Duration\")", source, StringComparison.Ordinal);
        Assert.Contains("var optionalRow = new WrapPanel", source, StringComparison.Ordinal);
        Assert.Contains("removeButton.Margin = new Thickness(2, 0, 0, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"+ Add Rate\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BurstHeal_UsesPostRateAnimationOptionalAttachmentAndDeferredOnHitEffects()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("DefaultVisibleTags: [\"anim\", \"maxrange\", \"rof\", \"rate\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("var isBurstHeal = actionType.Equals(\"BurstHeal\"", source, StringComparison.Ordinal);
        var attachmentTypesStart = source.IndexOf("OptionalModelAttachmentActionTypes", StringComparison.Ordinal);
        var attachmentTypesEnd = source.IndexOf("private static bool SupportsOptionalModelAttachmentActionType", attachmentTypesStart, StringComparison.Ordinal);
        Assert.True(attachmentTypesStart >= 0 && attachmentTypesEnd > attachmentTypesStart);
        Assert.Contains("\"BurstHeal\"", source[attachmentTypesStart..attachmentTypesEnd], StringComparison.Ordinal);
        var whitelistStart = source.IndexOf("ProtoActionTypesShowingOnHitEffectByDefault", StringComparison.Ordinal);
        var whitelistEnd = source.IndexOf("private static bool ShouldShowProtoActionOnHitEffectByDefault", whitelistStart, StringComparison.Ordinal);
        Assert.True(whitelistStart >= 0 && whitelistEnd > whitelistStart);
        Assert.DoesNotContain("\"BurstHeal\"", source[whitelistStart..whitelistEnd], StringComparison.Ordinal);
        Assert.Contains("Content = \"Add On Hit Effect\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"+ Add On Hit Effect\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChargedAndConditionalShieldAttachments_UseCompactSharedWidths()
    {
        var source = ReadProtoEditorSource();
        var attachmentSource = ReadAttachmentEditorSource();

        Assert.Contains("new AttachmentEditor(", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Bone:\"", source, StringComparison.Ordinal);
        Assert.Contains("attachmentLabel + \":\"", source, StringComparison.Ordinal);
        Assert.Contains("boneLabel + \":\"", source, StringComparison.Ordinal);
        Assert.Contains("AttachmentWidth = 200", attachmentSource, StringComparison.Ordinal);
        Assert.Contains("BoneWidth = 100", attachmentSource, StringComparison.Ordinal);
        Assert.Contains("field.HorizontalAlignment = HorizontalAlignment.Left;", attachmentSource, StringComparison.Ordinal);
        Assert.Contains(".Where(HasAttachmentLabel)", source, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center", attachmentSource, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"targetattachmentbone\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"infectionattachmentbone\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("(!_isReadOnly || state.SelectedFlagTags.Contains(\"modelattachmentonself\"))", source, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = !_isReadOnly", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoGatherAndModifyGather_ShareOnlyTheResourceRatePresentation()
    {
        var source = ReadProtoEditorSource();
        var modifyGatherStart = source.IndexOf("private void RenderModifyGatherRateSection(", StringComparison.Ordinal);
        var modifyGatherEnd = source.IndexOf("private void RenderProtoActionAdditionalFields(", modifyGatherStart, StringComparison.Ordinal);
        var autoGatherStart = source.IndexOf("else if (IsAutoGatherActionType(actionType))", StringComparison.Ordinal);
        var autoGatherEnd = source.IndexOf("else if (IsConvertActionType(actionType))", autoGatherStart, StringComparison.Ordinal);

        Assert.True(modifyGatherStart >= 0 && modifyGatherEnd > modifyGatherStart);
        Assert.True(autoGatherStart >= 0 && autoGatherEnd > autoGatherStart);
        var modifyGather = source[modifyGatherStart..modifyGatherEnd];
        var autoGather = source[autoGatherStart..autoGatherEnd];

        Assert.Contains("new RateEditor(", modifyGather, StringComparison.Ordinal);
        Assert.Contains("new RateEditor(", autoGather, StringComparison.Ordinal);
        Assert.Contains("BlockInheritedStructuredProtoActionEntryRemovalAsync", modifyGather, StringComparison.Ordinal);
        Assert.Contains("BlockInheritedStructuredProtoActionEntryRemovalAsync", autoGather, StringComparison.Ordinal);
        Assert.Contains("RefreshResourceChoices();", modifyGather, StringComparison.Ordinal);
        Assert.Contains("RefreshAutoGatherResourceChoices();", autoGather, StringComparison.Ordinal);
    }

    [Fact]
    public void EmpowerRates_UseStrictDamageTypeProfilesAndTheSharedModifierRow()
    {
        var source = ReadProtoEditorSource();
        var start = source.IndexOf("private void RenderProtoActionEmpowerSections(", StringComparison.Ordinal);
        var end = source.IndexOf("private void RenderProtoActionChargedFields(", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var empower = source[start..end];
        Assert.Contains("var damageTypeCb = new ComboBox", empower, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(damageTypeCb", empower, StringComparison.Ordinal);
        Assert.Contains("new ModifierEditor(", empower, StringComparison.Ordinal);
        Assert.Contains("DamageTypeCb = damageTypeCb", empower, StringComparison.Ordinal);
        Assert.Contains("rateRow.DamageTypeCb.SelectedItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainDamageRows_UseSharedPresentationWithoutMovingDamageSemantics()
    {
        var source = ReadProtoEditorSource();
        Assert.Contains("var rowPanel = new DamageEditor(typeCb, valTb, showRemoveButton: !_isReadOnly);", source, StringComparison.Ordinal);
        Assert.Contains("rowPanel.SetSourceMarker(CreateProtoActionSourceMarker(source, tacticsAmount));", source, StringComparison.Ordinal);
        Assert.Contains("RefreshDamageTypeOptions();", source, StringComparison.Ordinal);
        Assert.Contains("state.DamageRows.Add(rowState);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformConvertAndChargedCards_KeepUiLabelsSeparateFromXmlValues()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Text = \"Transform into:\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Enemy In Range\"", source, StringComparison.Ordinal);
        Assert.Contains(".Replace(\" \", \"\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("rateSection.IsVisible = string.Equals(", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Must Finish Animation\"", source, StringComparison.Ordinal);

        Assert.Contains("Text = \"Duration by type:\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Stun duration by Type\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Transform into:\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Min Range\"", source, StringComparison.Ordinal);
        Assert.Contains("MoveConvertAnimationBeforeAttachment(state, actionType);", source, StringComparison.Ordinal);

        Assert.Contains("chargedElements.Count > 1", source, StringComparison.Ordinal);
        Assert.Contains("state.FullChargedRows.RemoveAt(cardIndex);", source, StringComparison.Ordinal);
        Assert.Contains("new ChargedEditor(", source, StringComparison.Ordinal);
        Assert.Contains("card.RemoveButton.Click +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("cardStack.Children.Add(cardHeader);", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"conditionalshield.attachment-buttons\"", source, StringComparison.Ordinal);
        Assert.Contains("KeepAutoCompleteTextStartVisible(nameAcb);", source, StringComparison.Ordinal);
        Assert.Contains("KeepAutoCompleteTextStartVisible(typeAcb);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DelayedTransform_UsesCompactTransformFirstAnimationLastLayout()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Text = \"Time to transform (ms):\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Transform On Attack\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Must Finish Animation\"", source, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, 220, Auto, Auto\")", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Add(animRow);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Transform to:\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevotionActions_UseGroupedOptionalRowsAndStrictResourceRate()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("ArrangeDevoteMajorLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Animation\"", source, StringComparison.Ordinal);
        Assert.Contains("IsDevoteMajorActionType(actionType) && tag.Equals(\"devotiontime\"", source, StringComparison.Ordinal);

        Assert.Contains("Text = \"Type:\"", source, StringComparison.Ordinal);
        Assert.Contains("var rateTypeAcb = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Scaling Reduction\"", source, StringComparison.Ordinal);
        Assert.Contains("AddDevoteMinorTextOption(\"devotionpower\", \"Devotion Power\"", source, StringComparison.Ordinal);
        Assert.Contains("AddDevoteMinorTextOption(\"soundsetenter\", \"Soundset Enter\"", source, StringComparison.Ordinal);
        Assert.Contains("editor.Width = 200;", source, StringComparison.Ordinal);

        Assert.Contains("AddPrayOptional(\"devotionhealthdraineachsecond\", \"HP Drained (s)\")", source, StringComparison.Ordinal);
        Assert.Contains("AddPrayOptional(\"devotionhealthdrainlimit\", \"HP Cap\")", source, StringComparison.Ordinal);
        Assert.Contains("AddPrayOptional(\"devotionscaleatminimumhealth\", \"Scale at Min Health\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevotionActions_UseSharedDeferredAttachmentsAndLastVisualRow()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("\"DevoteMinor\",", source, StringComparison.Ordinal);
        Assert.Contains("ArrangeDevoteMinorLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"devoteminor.animation\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"devoteminor.visuals\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultVisibleTags: [\"anim\", \"devotiontime\", \"maxrange\", \"modelattachment\"", metadata, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, Auto\")", source, StringComparison.Ordinal);

        var scalingIndex = source.IndexOf("Content = \"Scaling Reduction\"", StringComparison.Ordinal);
        var powerIndex = source.IndexOf("AddDevoteMinorTextOption(\"devotionpower\"", scalingIndex, StringComparison.Ordinal);
        var soundsetIndex = source.IndexOf("AddDevoteMinorTextOption(\"soundsetenter\"", powerIndex, StringComparison.Ordinal);
        Assert.True(scalingIndex >= 0 && powerIndex > scalingIndex && soundsetIndex > powerIndex);
    }

    [Fact]
    public void DistanceModify_UsesStrictDamageChoicesAndSignedValues()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("CreateLabeledFieldGroup(\"Modify:\", modifyTypeAcb)", source, StringComparison.Ordinal);
        Assert.Contains("var modifyDamageTypeAcb = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(modifyDamageTypeAcb", source, StringComparison.Ordinal);
        Assert.Contains("\"Damage type:\"", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionDecimalBehavior(modifyAmountTb, () => true)", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionDecimalBehavior(modifyMultiplierTb, () => true)", source, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, 140, 32\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DrainDropOffAndEat_UseRequestedOptionalRows()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("ArrangeDrainResurrectionStructuredLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("MoveStructuredSectionIntoAdditionalFields(state, \"Rate:\", 3)", source, StringComparison.Ordinal);
        Assert.Contains("SyncDrainResurrectionDamageFlagsStorage", source, StringComparison.Ordinal);
        Assert.Contains("var value = string.Join(\"|\"", source, StringComparison.Ordinal);
        Assert.Contains("ProtoXmlHandler.SetProtoActionSimpleFieldValue(state.Model, \"damageflags\", value);", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Add(animProjectileRow);", source, StringComparison.Ordinal);
        Assert.Contains("ArrangeEatLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Animation\"", source, StringComparison.Ordinal);
        Assert.Contains("state.StructuredFieldsContainer.Children.Add(addAnimation);", source, StringComparison.Ordinal);
        Assert.Contains("[\"DropOff\"] = new ProtoActionTypeEditorProfile", ReadProtoActionMetadataSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void DistanceAndDrain_UseSharedRepeatableStructuredRates()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("private static bool IsManagedDrainResurrectionStructuredFieldTag", source, StringComparison.Ordinal);
        Assert.Contains("private static bool IsManagedDistanceModifyStructuredFieldTag", source, StringComparison.Ordinal);
        Assert.Contains("MoveStructuredSectionIntoAdditionalFields(state, \"Min Rate:\", 1)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"minrate\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("!IsDistanceModifyActionType(actionType)", source, StringComparison.Ordinal);
        Assert.Contains("rowState.PreservedAttributes[attribute.Key] = attribute.Value;", source, StringComparison.Ordinal);
        Assert.Contains("ProtoXmlHandler.SetProtoActionSimpleFieldValue(pa, \"damageflags\", currentDamageFlags);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var showDrainRate =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Empower_UsesDeferredCompactSectionsAndSharedAttachmentPattern()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("ArrangeEmpowerLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("Content = $\"Add {GetSectionLabel(sectionTag)} Target\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Model Attachment\"", source, StringComparison.Ordinal);
        Assert.Contains("targetState.ModelAttachmentBoneAcb,", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Empower Rate\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Modify:\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Empower Area:\"", source, StringComparison.Ordinal);
        Assert.Contains("targetAcb.Width = 200;", source, StringComparison.Ordinal);
        Assert.Contains("acb.Width = 200;", source, StringComparison.Ordinal);
        Assert.Contains("var emptyAttachmentEditors = CreateProtoActionModelAttachmentEditors(\"\", \"\");", source, StringComparison.Ordinal);
        Assert.Contains("targetState.ModelAttachmentEditor = emptyAttachmentEditors.Attachment;", source, StringComparison.Ordinal);
        Assert.Contains("targetState.ModelAttachmentBoneAcb = emptyAttachmentEditors.Bone;", source, StringComparison.Ordinal);
        Assert.Contains("targetState.IsModelAttachmentVisible = false;", source, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, Auto, Auto\")", source, StringComparison.Ordinal);
        Assert.Contains("forbidContainer.Children.Insert(buttonIndex, row);", source, StringComparison.Ordinal);
        Assert.Contains("visualOptionsRow.Children.Add(specificAnimationCell);", source, StringComparison.Ordinal);
        Assert.Contains("visualOptionsRow.Children.Add(attachmentHost);", source, StringComparison.Ordinal);
        Assert.Contains("if (!_isReadOnly && targetState.RateRows.Count == 0)", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Insert(0, animationRow);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"+ Empower Rate\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = $\"+ Add {GetSectionLabel(sectionTag)} Target\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GatherAndHeal_UseCompactDeferredSharedLayouts()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("rangeRow.Children.Add(CreateLabeledFieldGroup(\"Max Range:\", maxRangeMirror));", source, StringComparison.Ordinal);
        Assert.Contains("dropsiteGroup.Children.Add(CreateSimpleEditor(\"dropsitegathering\"));", source, StringComparison.Ordinal);
        Assert.Contains("var isGatherOptionalRateAttribute", source, StringComparison.Ordinal);
        Assert.Contains("? \"Resource:\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"Override Resource:\"", source, StringComparison.Ordinal);
        Assert.Contains("var attributeCombo = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("SelectedItem = attributeSuggestions.FirstOrDefault", source, StringComparison.Ordinal);
        Assert.Contains("? \"Add Animation by Type\"", source, StringComparison.Ordinal);
        Assert.Contains(": \"Add Max Range by Type\"", source, StringComparison.Ordinal);
        Assert.Contains("actionType.Equals(\"Hunting\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);

        Assert.Contains("(!IsConvertActionType(actionType) && !IsHealActionType(actionType))", source, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"60, Auto, 180, 100, 32\")", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Add(slowHealRow);", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Efficency:\", efficiencyEditor)", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Radius:\", radiusEditor)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Target Limit\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleStatBonusAndModifyGather_UseStrictCompactEditors()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Text = \"Modify:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Damage Type:\"", source, StringComparison.Ordinal);
        Assert.Contains("var modifyDamageTypeAcb = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(modifyDamageTypeAcb", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Modify Amount\", \"Modify Multiplier\" }", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Rate Cap:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Decay:\"", source, StringComparison.Ordinal);
        Assert.Contains("var trailingModifyFields = new StackPanel", source, StringComparison.Ordinal);
        Assert.Contains("modifyRow.Children.Add(trailingModifyFields);", source, StringComparison.Ordinal);

        Assert.Contains("RenderModifyGatherRateSection(state, effectiveAction, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Resource Sub Type\"", source, StringComparison.Ordinal);
        Assert.Contains("var resourceSubTypeEditor = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("CollectProtoActionStructuredFieldEntries(state, \"rate\")", source, StringComparison.Ordinal);
        Assert.Contains("fieldTags.RemoveAll(tag => tag.Equals(\"rate\"", source, StringComparison.Ordinal);
        Assert.Contains("addRateButton.IsVisible = ProtoConstants.KnownResourceTypes.Any", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GatherNoWorkRepairAndReflectAttack_UseSharedActionCardLayouts()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("var isGather = IsGatherActionType(actionType);", source, StringComparison.Ordinal);
        Assert.Contains("var isNoWork = actionType.Equals(\"NoWork\"", source, StringComparison.Ordinal);
        Assert.Contains("actionType.Equals(\"Repair\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalButton(\"Add Animation by Type\", \"typedanim\")", source, StringComparison.Ordinal);
        Assert.Contains("CreateOptionalButton(\"Add Max Range by Type\", \"typedmaxrange\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLabeledFieldGroup(\"Default animation:\", animEditor)", source, StringComparison.Ordinal);
        Assert.Contains("maxRangeRowIndex >= 0 ? maxRangeRowIndex + 1 : 0", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Reflects attacks\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Pickup\",\n        \"Hunting\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ReflectAttack\",\n        \"WaterTornado\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"Repair\"] = new ProtoActionTypeEditorProfile(\n            DefaultVisibleTags: [\"anim\", \"maxrange\", \"rate\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("[\"Gather\"] = new ProtoActionTypeEditorProfile(\n            DefaultVisibleTags: [\"dropsitegathering\", \"maxrange\", \"rate\", \"typedanim\", \"typedmaxrange\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("IsGatherActionType(actionType) &&\n                          x.Equals(\"anim\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StackControl_UsesSingletonActionReferencesAndDerivedUnitAttribute()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("[\"StackControl\"] = new ProtoActionTypeEditorProfile(\n            DefaultVisibleTags: []", metadata, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"anim\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("ComboBox CreateActionSelector", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = availableActionNames", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Add Action:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Sub Action:\"", source, StringComparison.Ordinal);
        Assert.Contains("IsStackControlTypeClaimedByAnother", source, StringComparison.Ordinal);
        Assert.Contains("RefreshStackControlTypeOptions", source, StringComparison.Ordinal);
        Assert.Contains("HasStackControlConflict", source, StringComparison.Ordinal);
        Assert.Contains("A unit can contain only one StackControl action.", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate StackControl Actions", source, StringComparison.Ordinal);
        Assert.Contains("state.TypeValidationFrame.BorderBrush = Brush.Parse(\"#d64545\");", source, StringComparison.Ordinal);
        Assert.Contains("stackControlConflicts.Count <= 1", source, StringComparison.Ordinal);
        Assert.Contains("SyncStackProtoActionReference(unit, stackControlProtoActionName);", source, StringComparison.Ordinal);
        Assert.Contains("!x.Equals(\"stackprotoaction\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("SyncManagedProtoActionStatsEditor(tag, actionName);", source, StringComparison.Ordinal);
        Assert.Contains("This reference is managed automatically by its ProtoAction.", source, StringComparison.Ordinal);
        Assert.Contains("RefreshStackProtoActionReference();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BirthAndSelfDestructAttacks_AreSingletonsWithManagedStatsReferences()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("HasExclusiveAttackModeConflict", source, StringComparison.Ordinal);
        Assert.Contains("IsExclusiveAttackMode(attackMode)", source, StringComparison.Ordinal);
        Assert.Contains("IsAttackModeClaimedByAnother(state, attackMode)", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate {exclusiveMode} Actions", source, StringComparison.Ordinal);
        Assert.Contains("RefreshExclusiveAttackActionReferences();", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"selfdestructprotoaction\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"birthprotoaction\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("if (!_isReadOnly && !isManagedProtoActionReference)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoBoostAndAttackVisualFields_UseCompactSharedWidthsAndDefaults()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("new ColumnDefinitions(\"Auto, 200, 32\")", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProtoActionFieldWidth(editor, tag);", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"impacteffect\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"launchpoint\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"castpower\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("castPowerTargetValue = \"Unit\";", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Area Sort\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"+ Area Sort\"", source, StringComparison.Ordinal);
        Assert.Contains("EditorTextFieldStyle.ConfigureTextBox(castPowerEditor);\n                    ApplyProtoActionFieldWidth(castPowerEditor, \"castpower\");", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(castPowerTargetEditor, castPowerTargetSuggestions, castPowerTargetEditor.Text ?? \"\");\n                        ApplyProtoActionFieldWidth(castPowerTargetEditor, \"castpower\");", source, StringComparison.Ordinal);
        Assert.Contains("modifyProtoAcb.MinWidth = 200;\n                modifyProtoAcb.MaxWidth = 200;", source, StringComparison.Ordinal);
        Assert.Contains("[\"selfdestructprotoaction\"] = \"Self Destruct Action\"", source, StringComparison.Ordinal);
        Assert.Contains("[\"birthprotoaction\"] = \"Birth Action\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnHitEffects_UsePairedOptionalControlsAndChipTargetFilters()
    {
        var source = ReadProtoEditorSource();
        var onHitStart = source.IndexOf("private void RenderProtoActionOnHitEffects(", StringComparison.Ordinal);
        var onHitEnd = source.IndexOf("private void RenderProtoActionStructuredFields(", onHitStart, StringComparison.Ordinal);
        Assert.True(onHitStart >= 0 && onHitEnd > onHitStart);
        var onHit = source[onHitStart..onHitEnd];

        Assert.Contains("CreateOnHitLabeledControlGroup", onHit, StringComparison.Ordinal);
        Assert.Contains("new OnHitEffectEditor(", onHit, StringComparison.Ordinal);
        Assert.Contains("if (card.RemoveButton != null)", onHit, StringComparison.Ordinal);
        Assert.Contains("InsertOptionalLabeledControl(\"Probability:\"", onHit, StringComparison.Ordinal);
        Assert.Contains("InsertOptionalLabeledControl(\"Global Probability:\"", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Animation:\", animAcb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Bone:\", attachBoneAcb", onHit, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"Attach Bone\"", onHit, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Simple\", \"Multiple\" }", onHit, StringComparison.Ordinal);
        Assert.Contains("new TargetFilterEditor(", onHit, StringComparison.Ordinal);
        Assert.Contains("targetFiltersSection.RefreshModeVisibility();", onHit, StringComparison.Ordinal);
        Assert.Contains("currentSupportedType.Equals(\"SelfModify\"", onHit, StringComparison.Ordinal);
        Assert.Contains("currentSupportedType.Equals(\"SelfStealth\"", onHit, StringComparison.Ordinal);
        Assert.Contains("var addTargetFiltersSection = !targetsSelf", onHit, StringComparison.Ordinal);
        Assert.Contains("CreateTargetFilterChipEditor(\"Attack Type\"", onHit, StringComparison.Ordinal);
        Assert.Contains("CreateTargetFilterChipEditor(\"Ignore Type\"", onHit, StringComparison.Ordinal);
        Assert.Contains("if (!usesMultipleTargets && !string.IsNullOrWhiteSpace(targetUnitType))", source, StringComparison.Ordinal);
        Assert.Contains("if (usesMultipleTargets)", source, StringComparison.Ordinal);
        Assert.Contains(".Concat(GetAvailableTrainUnitNames())", onHit, StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(\n                protoActionAcb,\n                protoActionNameSuggestions", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Infect Action:\", protoActionAcb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Radius:\", radiusTb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("rowGrid.ColumnDefinitions[3].Width = visible ? GridLength.Auto : new GridLength(0);", onHit, StringComparison.Ordinal);
        Assert.Contains("shadingTypeAcb.Text = KnownInitialShadingTypes.FirstOrDefault() ?? \"Default\";", onHit, StringComparison.Ordinal);
        Assert.Contains("probTb.Text = \"0\";", onHit, StringComparison.Ordinal);
        Assert.Contains("globalProbTb.Text = \"0\";", onHit, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionProbabilityBehavior(amountTb, 1d);", onHit, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Set\", \"Add\", \"Multiply\" }", onHit, StringComparison.Ordinal);
        Assert.Contains("ItemsSource = new[] { \"Land\", \"Water\" }", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Proto:\", attachProtoAcb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Rate:\", rateTb);\n                    if (currentSupportedType.Equals(\"Snare\"", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Factor:\", factorTb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("\"Pull\", \"Push\", \"Root\"", source, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Rate:\", amountTb);\n                    AddLabeledControl(\"Proto:\", attachProtoAcb);\n                    AddLabeledControl(\"Duration:\", durationTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("effectType.Equals(\"Root\"", onHit, StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(attachProtoAcb, protoUnitSuggestions", onHit, StringComparison.Ordinal);
        Assert.Contains("currentSupportedType.Equals(\"Push\"", onHit, StringComparison.Ordinal);
        Assert.Contains("AddLabeledControl(\"Force:\", radiusTb);", onHit, StringComparison.Ordinal);
        Assert.Contains("KnownOnHitEffectFreezeTypeDisplayNames", source, StringComparison.Ordinal);
        Assert.Contains("GetOnHitEffectFreezeTypeXmlValue", source, StringComparison.Ordinal);
        Assert.Contains("effectType.Equals(\"InstantKillablePercentChance\", StringComparison.OrdinalIgnoreCase) &&\n                !string.IsNullOrWhiteSpace(relativity)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Stack\"", onHit, StringComparison.Ordinal);
        Assert.Contains("CreateOnHitLabeledControlGroup(\"Stack:\", stackLimitTb", onHit, StringComparison.Ordinal);
        Assert.Contains("deferPrimaryRowsUntilAfterDamage", onHit, StringComparison.Ordinal);
        Assert.Contains("CommitChipAdditionAndRefreshXmlPreview();", onHit, StringComparison.Ordinal);
        Assert.Contains("var damageRowsHost = new WrapPanel", onHit, StringComparison.Ordinal);
        Assert.Contains("var damageTypeCb = new ComboBox", onHit, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Damage\"", onHit, StringComparison.Ordinal);
        Assert.Contains("usedByOtherRows", onHit, StringComparison.Ordinal);
        Assert.Contains("storedValues.Remove(storedValue);", onHit, StringComparison.Ordinal);
        Assert.Contains("AddChipValue(value);", onHit, StringComparison.Ordinal);
        Assert.Contains("CreateOnHitEffectTypeReplacement(selectedType", onHit, StringComparison.Ordinal);
    }

    [Fact]
    public void FullChargedEditor_UsesTheSharedContainerShell()
    {
        var source = ReadProtoEditorSource();
        var start = source.IndexOf("private void RenderProtoActionFullChargedFields(", StringComparison.Ordinal);
        var end = source.IndexOf("private List<ProtoActionOnHitEffectEntry> CollectProtoActionOnHitEffectEntries", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var charged = source[start..end];

        Assert.Contains("new ChargedEditor(", charged, StringComparison.Ordinal);
        Assert.Contains("showRemoveButton: chargedElements.Count > 1", charged, StringComparison.Ordinal);
        Assert.Contains("if (card.RemoveButton != null)", charged, StringComparison.Ordinal);
        Assert.Contains("if (IsBolsterActionType(actionType))", charged, StringComparison.Ordinal);
    }

    [Fact]
    public void OnHitEffectTypeReplacement_DropsAttributesOwnedByThePreviousType()
    {
        var replacement = ProtoEditorWindow.CreateOnHitEffectTypeReplacement("Attach", active: true);

        Assert.Equal("Attach", replacement.Attribute("type")?.Value);
        Assert.Null(replacement.Attribute("anim"));
        Assert.Null(replacement.Attribute("duration"));
        Assert.Null(replacement.Attribute("targetunittype"));
        Assert.Null(replacement.Attribute("active"));

        var inactive = ProtoEditorWindow.CreateOnHitEffectTypeReplacement("AnimOverride", active: false);
        Assert.Equal("0", inactive.Attribute("active")?.Value);
    }

    [Fact]
    public void NewAction_RequiresANameBeforeTypeSelection()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("var hasActionName = !string.IsNullOrWhiteSpace(actionName);", source, StringComparison.Ordinal);
        Assert.Contains("!hasDuplicate && hasActionName", source, StringComparison.Ordinal);
        Assert.Contains("Enter an action name before choosing its type.", source, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(actionName))", source, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(nameAcb.Text))", source, StringComparison.Ordinal);
        Assert.Contains("typeAcb.IsDropDownOpen = false;", source, StringComparison.Ordinal);
        var nameChangedStart = source.IndexOf("nameAcb.TextChanged += async", StringComparison.Ordinal);
        var synchronousTypeRefresh = source.IndexOf("UpdateProtoActionTypeEditor(typeAcb, name);", nameChangedStart, StringComparison.Ordinal);
        var localModCheck = source.IndexOf("var proceed = await CheckStartLocalMod();", nameChangedStart, StringComparison.Ordinal);
        Assert.True(nameChangedStart >= 0 && synchronousTypeRefresh > nameChangedStart);
        Assert.True(localModCheck > synchronousTypeRefresh);
        Assert.Contains("void ApplyProtoActionTypeSelection(string selectedActionType)", source, StringComparison.Ordinal);
        Assert.Contains("typeAcb.SelectionChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProtoActionTypeSelection(selectedType);", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(async () =>", source, StringComparison.Ordinal);
        Assert.Contains("var alreadyRendered = selectedActionType.Equals(lastRenderedActionType", source, StringComparison.Ordinal);
        Assert.Contains("var currentText = actionState.TypeAcb.Text?.Trim() ?? \"\";", source, StringComparison.Ordinal);
        Assert.Contains("if (!state.IsNewCustomAction &&", source, StringComparison.Ordinal);
        Assert.Contains("if (!name.Equals(nameAcb.Text?.Trim() ?? \"\", StringComparison.Ordinal))", source, StringComparison.Ordinal);
        Assert.Contains("nameAcb.DropDownClosed +=", source, StringComparison.Ordinal);
        Assert.Contains("void CommitActionNameSelection()", source, StringComparison.Ordinal);
        var typeTextChangedStart = source.IndexOf("typeAcb.TextChanged += async", nameChangedStart, StringComparison.Ordinal);
        var typeLostFocusStart = source.IndexOf("typeAcb.LostFocus +=", typeTextChangedStart, StringComparison.Ordinal);
        Assert.True(typeTextChangedStart >= 0 && typeLostFocusStart > typeTextChangedStart);
        Assert.Contains("ApplyProtoActionTypeSelection(selectedActionType);", source[typeTextChangedStart..typeLostFocusStart], StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchTradeTrailAndWaterTornado_UseRequestedCompactDeferredLayouts()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("actionType.Equals(\"SwitchTactic\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchTactics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchTactics", metadata, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"anim\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, 200\")", source, StringComparison.Ordinal);

        Assert.DoesNotContain("AddTradeOptionalSimpleRow(\"anim\", \"Animation\")", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Grant Other Resources\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Grant Resource to Ally\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Grants:\"", source, StringComparison.Ordinal);
        Assert.Contains("var resourceCombo = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add other resource\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Bonus Gold Factor:\"", source, StringComparison.Ordinal);
        Assert.Contains("var otherResourceEntries = GetCurrentStructuredEntries(\"minrate\")", source, StringComparison.Ordinal);
        Assert.Contains("entry.Attributes[\"type\"] = NormalizeTradeOtherResourceType(resourceType);", source, StringComparison.Ordinal);
        Assert.Contains("!resource.Equals(\"Gold\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("state.StructuredFieldRows[\"minrate\"].Count < tradeOtherResourceTypes.Length", source, StringComparison.Ordinal);
        Assert.Contains("!usedByOtherRows.Contains(resource)", source, StringComparison.Ordinal);
        Assert.Contains("grantsContainer.Children.Insert(addButtonIndex, row);", source, StringComparison.Ordinal);

        Assert.Contains("Text = \"Trail Proto Unit:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Frequency(s):\"", source, StringComparison.Ordinal);
        Assert.Contains("parsedFrequency > 0", source, StringComparison.Ordinal);
        Assert.Contains("frequencyEditor.ClearValue(TemplatedControl.BorderBrushProperty);", source, StringComparison.Ordinal);
        Assert.Contains("Invalid Trail Frequency", source, StringComparison.Ordinal);

        Assert.DoesNotContain("\"TrapThrow\",\n        \"WaterTornado\"", source, StringComparison.Ordinal);
        Assert.Contains("var isWaterTornado = actionType.Equals(\"WaterTornado\"", source, StringComparison.Ordinal);
        Assert.Contains("AddSpreadField(\"maxspread\", \"Max Spread\", false);", source, StringComparison.Ordinal);
        Assert.Contains("AddSpreadField(\"spreadfactor\", \"Spread Factor\", true);", source, StringComparison.Ordinal);
        Assert.Contains("DefaultVisibleTags: [\"maxrange\", \"maxspread\", \"spreadfactor\"]", metadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Food", "food")]
    [InlineData(" GOLD ", "gold")]
    [InlineData("favor", "favor")]
    public void TradeOtherResourceType_IsSerializedInTheGameExpectedLowerCase(string input, string expected)
    {
        Assert.Equal(expected, ProtoEditorWindow.NormalizeTradeOtherResourceType(input));
    }

    [Fact]
    public void TradeOtherResource_UsesMinRateXmlRatherThanMinWorkRate()
    {
        var action = new ProtoAction();
        var entry = new ProtoActionStructuredFieldEntry { Value = "0.1" };
        entry.Attributes["type"] = ProtoEditorWindow.NormalizeTradeOtherResourceType("Food");

        ProtoXmlHandler.SetProtoActionStructuredFieldEntries(action, "minrate", [entry]);

        var xml = Assert.Single(action.AdditionalElements);
        Assert.Equal("minrate", xml.Name.LocalName);
        Assert.Equal("food", xml.Attribute("type")?.Value);
        Assert.Equal("0.1", xml.Value);
        Assert.DoesNotContain(action.AdditionalElements, element =>
            element.Name.LocalName.Equals("minworkrate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThrowTeleportAndSpawnActions_UseTheRequestedRateAndAnimationLayouts()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.DoesNotContain("Content = \"Do Not Work On Frozen Units\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"throw.killoninvalidland\"", source, StringComparison.Ordinal);
        Assert.Contains("Width = new GridLength(8)", source, StringComparison.Ordinal);
        Assert.Contains("(\"throwdistancemax\", \"Max\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"throwmaxheight\", \"Height Max\")", source, StringComparison.Ordinal);
        Assert.Contains("IsThrowActionType(actionType) ||\n              IsSpawnAssistActionType(actionType)", source, StringComparison.Ordinal);

        Assert.Contains("IsTeleportAttackActionType(actionType) || isWaterTornado", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Duration\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Duration:\"", source, StringComparison.Ordinal);
        Assert.Contains("AddVisualField(\"anim\", \"Animation\", animation, \"TeleportStart\");", source, StringComparison.Ordinal);
        Assert.Contains("AddVisualField(\"reloadanim\", \"Reload Animation\", reloadAnimation, \"TeleportEnd\");", source, StringComparison.Ordinal);
        Assert.Contains("AddVisualField(\"impacteffect\", \"Impact Effect\", impactEffect, \"\");", source, StringComparison.Ordinal);
        Assert.Contains("Text = string.IsNullOrWhiteSpace(value) ? defaultValue : value", source, StringComparison.Ordinal);
        Assert.Contains("editor.MinWidth = 200;", source, StringComparison.Ordinal);
        Assert.Contains("editor.MaxWidth = 200;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultVisibleTags: [\"anim\", \"reloadanim\", \"minrange\", \"maxrange\", \"rof\", \"rate\", \"accuracy\"", metadata, StringComparison.Ordinal);

        Assert.Contains("Text = \"Spawn At Target:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Spawned unit:\"", source, StringComparison.Ordinal);
        Assert.Contains("void AddSpawnAtTargetRow", source, StringComparison.Ordinal);
        Assert.Contains("void AddSpawnedUnitRow", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(protoUnitEditor, GetAvailableTrainUnitNames()", source, StringComparison.Ordinal);
        Assert.Contains("var rateEntries = GetCurrentStructuredEntries(\"rate\");", source, StringComparison.Ordinal);
        Assert.Contains("var minRateEntries = GetCurrentStructuredEntries(\"minrate\");", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Add(spawnedUnitsSection);\n                state.AdditionalFieldsContainer.Children.Add(spawnAtTargetSection);\n                AddSpawnOptionalSimpleRow(\"anim\", \"Animation\");", source, StringComparison.Ordinal);

        var customLayoutsStart = source.IndexOf("private void RenderProtoActionAdditionalFields(", StringComparison.Ordinal);
        var customLayoutsEnd = source.IndexOf("private void RenderProtoActionDamageExtras(", customLayoutsStart, StringComparison.Ordinal);
        Assert.True(customLayoutsStart >= 0 && customLayoutsEnd > customLayoutsStart);
        var customLayouts = source[customLayoutsStart..customLayoutsEnd];
        Assert.DoesNotContain("GetProtoActionStructuredFieldEntriesForEditor(effectiveAction, actionType, \"", customLayouts, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestedCombatActions_DefaultToImpactEffectAndThrowUsesSeparateMaxSizeClassOption()
    {
        foreach (var actionType in new[]
                 {
                     "Attack", "ChainAttack", "Gore", "Throw", "TeleportAttack", "BuckAttack",
                     "JumpAttack", "LinearAreaAttack", "ReflectAttack", "AoEAttack", "Hunting", "Lure"
                 })
        {
            Assert.Contains(
                "impacteffect",
                ProtoActionMetadataCatalog.GetEditorProfile(actionType).DefaultVisibleTags,
                StringComparer.OrdinalIgnoreCase);
        }

        var source = ReadProtoEditorSource();
        Assert.Contains("tag.Equals(\"maxsizeclass\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Additional Throw Information\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Max Size Class\"", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldControls[\"maxsizeclass\"] = maxSizeClassEditor;", source, StringComparison.Ordinal);
        Assert.Contains("ArrangeTrailingImpactEffectLayout(state, actionType);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Rampage_ExposesDamageBonusCompactOptionsAndChargedModifiers()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("IsRampageActionType(actionType) ||\n                                                 IsAutoBoostActionType(actionType) ||\n                                                 ShouldShowProtoActionHardcodedField", source, StringComparison.Ordinal);
        Assert.Contains("state.RampageLayoutContainer.Children.Add(state.BonusSectionContainer);", source, StringComparison.Ordinal);
        Assert.Contains("AddCompactOption(maxSizeControl, 90);", source, StringComparison.Ordinal);
        Assert.Contains("AddCompactOption(projectileControl, 200);", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"protoaction.charged\"", source, StringComparison.Ordinal);
        Assert.Contains("BuildFullChargedElements(state, includeEmptyRows: true)", source, StringComparison.Ordinal);
        Assert.Contains("RenderProtoActionFullChargedFields(\n            state,", source, StringComparison.Ordinal);
        Assert.Contains("RenderProtoActionChargedFields(\n            state,", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsBolsterActionType(actionType))", source, StringComparison.Ordinal);
        Assert.Contains("if (IsBolsterActionType(currentActionType))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChargedMaulMaintainAndLure_UseRequestedCompactOrdering()
    {
        var source = ReadProtoEditorSource();

        var chargedStart = source.IndexOf("private void RenderProtoActionFullChargedFields(", StringComparison.Ordinal);
        var chargedEnd = source.IndexOf("private List<ProtoActionOnHitEffectEntry>", chargedStart, StringComparison.Ordinal);
        Assert.True(chargedStart >= 0 && chargedEnd > chargedStart);
        var charged = source[chargedStart..chargedEnd];
        Assert.Contains("usesSharedOptionalLayout", charged, StringComparison.Ordinal);
        Assert.Contains("Content = \"Activation Type\"", charged, StringComparison.Ordinal);
        Assert.Contains("Content = \"Cooldown\"", charged, StringComparison.Ordinal);
        Assert.Contains("Content = \"Duration\"", charged, StringComparison.Ordinal);
        Assert.Contains("Content = \"Remove charge\"", charged, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add Modifier\"", charged, StringComparison.Ordinal);
        Assert.Contains("ModifierRow = modifierRow", charged, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAddButton(\"+", charged, StringComparison.Ordinal);
        Assert.Contains("Width = 100", charged, StringComparison.Ordinal);

        Assert.Contains("IsMaulActionType(actionType) &&", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var tag in new[] { \"anim\", \"impacteffect\" })", source, StringComparison.Ordinal);
        Assert.Contains("state.StructuredFieldsContainer.Children.Add(row);", source, StringComparison.Ordinal);

        Assert.Contains("Text = \"Optional Flags:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Queue:\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Optional Flags\",\n                    FontWeight = FontWeight.Bold", source, StringComparison.Ordinal);

        Assert.Contains("Content = \"Min Range\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Exclusive\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"lure.rate\"", source, StringComparison.Ordinal);
        Assert.Contains("Tag = \"lure.animations\"", source, StringComparison.Ordinal);
        Assert.Contains("ArrangeLureLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("IsLureActionType(actionType) ||\n           IsRampageActionType(actionType)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LureLinearJumpGoreAndLikeBonus_ReuseCompactSharedControls()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("var rowPanel = new StackPanel\n                    {\n                        Orientation = Orientation.Horizontal,\n                        Spacing = 6", source, StringComparison.Ordinal);
        Assert.Contains("rateContainer.Children.Add(rowPanel);", source, StringComparison.Ordinal);

        Assert.Contains("\"impacteffect\",", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Impact Effect:\", CreateSimpleEditor(\"impacteffect\"))", source, StringComparison.Ordinal);
        var linearStart = source.IndexOf("if (IsLinearAreaAttackActionType(actionType))", StringComparison.Ordinal);
        var linearEnd = source.IndexOf("if (IsAutoRangedModifyActionType(actionType))", linearStart, StringComparison.Ordinal);
        Assert.True(linearStart >= 0 && linearEnd > linearStart);
        var linear = source[linearStart..linearEnd];
        Assert.True(linear.IndexOf("(\"walkanim\", \"Movement Anim\")", StringComparison.Ordinal) < linear.IndexOf("Content = \"Idle Anim\"", StringComparison.Ordinal));
        Assert.True(linear.IndexOf("Content = \"Idle Anim\"", StringComparison.Ordinal) < linear.IndexOf("CreateLabeledFieldGroup(\"Impact Effect:\"", StringComparison.Ordinal));

        Assert.Contains("!((IsJumpAttackActionType(actionType) || IsGoreActionType(actionType))", source, StringComparison.Ordinal);
        Assert.Contains("IsThrowActionType(actionType) || IsJumpAttackActionType(actionType) || IsGoreActionType(actionType)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var tag in new[] { \"anim\", \"impacteffect\" })", source, StringComparison.Ordinal);
        Assert.Contains("? new ColumnDefinitions(\"Auto, 200, Auto\")\n                    : new ColumnDefinitions(\"Auto, 200\")", source, StringComparison.Ordinal);

        Assert.Contains("modifyRow.Children.Add(CreateLabeledFieldGroup(\"Modify:\", modifyTypeAcb));", source, StringComparison.Ordinal);
        Assert.Contains("modifyRow.Children.Add(modeCombo);", source, StringComparison.Ordinal);
        Assert.Contains("modifyRow.Children.Add(modifierValueGroup);", source, StringComparison.Ordinal);
        Assert.Contains("modifierValueGroup.Margin = new Thickness(8, 0, 0, 0);", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"targetattachment\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("IsLikeBonusActionType(actionType) && attachmentButtons.Count == 2", source, StringComparison.Ordinal);
        Assert.Contains("assetPathEditor.Editor.MinWidth = fieldWidth.Value;", source, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(2, 0, 0, 0)", linear, StringComparison.Ordinal);
        Assert.Contains("impactEffectGroup.Margin = new Thickness(16, 0, 0, 0);", linear, StringComparison.Ordinal);

        var likeBonusStart = source.IndexOf("else if (IsLikeBonusActionType(actionType))", StringComparison.Ordinal);
        var likeBonusEnd = source.IndexOf("else if (IsSelfModifyActionType(actionType))", likeBonusStart, StringComparison.Ordinal);
        Assert.True(likeBonusStart >= 0 && likeBonusEnd > likeBonusStart);
        var likeBonus = source[likeBonusStart..likeBonusEnd];
        Assert.Contains("var damageTypeCombo = new ComboBox", likeBonus, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(damageTypeCombo, modifyType", likeBonus, StringComparison.Ordinal);
        Assert.Contains("modifyType is \"ArmorSpecific\" or \"DamageSpecific\"", likeBonus, StringComparison.Ordinal);
        Assert.Contains("modifyType.Equals(\"DamageByTargetType\"", likeBonus, StringComparison.Ordinal);
        Assert.Contains("GetKnownUnitTypeNames()\n                    .Concat(GetAvailableTrainUnitNames())", likeBonus, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldControls[\"modifydamagetargettype\"]", likeBonus, StringComparison.Ordinal);
        Assert.Contains("state.ForcedVisibleFieldTags.Remove(\"modifytargetlimit\")", likeBonus, StringComparison.Ordinal);
        Assert.Contains("ProtoXmlHandler.SetProtoActionSimpleFieldValue(state.Model, \"modifytargetlimit\", \"\")", likeBonus, StringComparison.Ordinal);

        Assert.Contains("\"impacteffect\",", source, StringComparison.Ordinal);
        Assert.Contains("autoCompleteBox.MinWidth = fieldWidth.Value;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfModify_UsesSharedDamageSelectorsAndTargetReferenceRules()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Text = \"Modify:\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Damage Type:\"", source, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(modifyDamageTypeAcb", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Target:\"", source, StringComparison.Ordinal);
        Assert.Contains("GetKnownUnitTypeNames()\n                    .Concat(GetAvailableTrainUnitNames())", source, StringComparison.Ordinal);
        Assert.Contains("modifyType.Equals(\"DamageByTargetType\"", source, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldControls[\"modifydamagetargettype\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoBoostAndOnHitEffects_UseOptionalCompactSharedControls()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("var modeOptions = new[] { \"Self\", \"Area\" };", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"None\", \"Self\", \"Area\"", source, StringComparison.Ordinal);
        Assert.Contains("state.CustomValues[AutoBoostSelfEnabledStateKey] = mode == \"Self\"", source, StringComparison.Ordinal);
        Assert.Contains("state.CustomValues[AutoBoostAreaEnabledStateKey] = mode == \"Area\"", source, StringComparison.Ordinal);
        Assert.Contains("AutoBoostResetOnHitEffectStateKey", source, StringComparison.Ordinal);
        Assert.Contains("var canAddAutoBoostDamageArea = isAutoBoost && IsAutoBoostAreaEnabled", source, StringComparison.Ordinal);
        Assert.Contains("IsAutoBoostActionType(actionType) && animationRow is Grid compactAnimationRow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"damage\", \"damagearea\", \"damageflags\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("\"modelattachment\", \"modelattachmentbone\"", metadata[metadata.IndexOf("[\"AutoBoost\"]", StringComparison.Ordinal)..metadata.IndexOf("[\"ChainAttack\"]", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("ArrangeAutoBoostPostRateLayout(state, actionType);", source, StringComparison.Ordinal);
        Assert.Contains("visualOptionsRow.Children.Add(attachmentControl);", source, StringComparison.Ordinal);
        Assert.Contains("visualOptionsRow.Children.Add(state.AutoBoostModifyProtoControl);", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"VFX:\"", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"VFX\"", source, StringComparison.Ordinal);
        Assert.Contains("state.OnHitEffectRows.Count > 0", source, StringComparison.Ordinal);

        var onHitStart = source.IndexOf("private void RenderProtoActionOnHitEffects(", StringComparison.Ordinal);
        var onHitEnd = source.IndexOf("private void RenderProtoActionStructuredFields(", onHitStart, StringComparison.Ordinal);
        Assert.True(onHitStart >= 0 && onHitEnd > onHitStart);
        var onHit = source[onHitStart..onHitEnd];
        Assert.Contains("Text = \"Modify:\"", onHit, StringComparison.Ordinal);
        Assert.Contains("Width = 100", onHit, StringComparison.Ordinal);
        Assert.Contains("var damageTypeCb = new ComboBox", onHit, StringComparison.Ordinal);
        Assert.Contains("RefreshProtoActionDamageTypeCombo(damageTypeCb", onHit, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Apply Type:\"", onHit, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"+", onHit, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoRangedModify_UsesCompactModifyTargetsAndSeparatedProtoButtons()
    {
        var source = ReadProtoEditorSource();
        var start = source.IndexOf("else if (IsAutoRangedModifyActionType(actionType)", StringComparison.Ordinal);
        var end = source.IndexOf("else if (IsConditionalShieldActionType(actionType))", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var autoRanged = source[start..end];

        Assert.Contains("Text = \"Modify:\"", autoRanged, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Modify Type:\"", autoRanged, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Damage Target Type:\"", autoRanged, StringComparison.Ordinal);
        Assert.Contains("Text = \"Target:\"", autoRanged, StringComparison.Ordinal);
        Assert.Contains("autoRangedUnitTypeSuggestions\n                    .Concat(GetAvailableTrainUnitNames())", autoRanged, StringComparison.Ordinal);
        Assert.Contains("var forbidProtoUnitButtonHost = targetProtoUnitRow != null", autoRanged, StringComparison.Ordinal);
        Assert.Contains("state.AdditionalFieldsContainer.Children.Add(forbidProtoUnitButtonHost);", autoRanged, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(10, 0, 12, 6)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StackControlReference_IsWrittenAndRemovedWithTheAction()
    {
        var unit = XElement.Parse("<unit name=\"Test\"><stackprotoaction>OldStack</stackprotoaction></unit>");

        ProtoEditorWindow.SyncStackProtoActionReference(unit, " NewStack ");
        Assert.Equal("NewStack", unit.Element("stackprotoaction")?.Value);

        ProtoEditorWindow.SyncStackProtoActionReference(unit, "");
        Assert.Null(unit.Element("stackprotoaction"));
    }

    [Theory]
    [InlineData("-5", 100d, "0")]
    [InlineData("25.5", 100d, "25.5")]
    [InlineData("125", 100d, "100")]
    [InlineData("-0.2", 1d, "0")]
    [InlineData("0.75", 1d, "0.75")]
    [InlineData("1.7", 1d, "1")]
    public void OnHitProbabilityNormalization_UsesTheRequestedRange(string input, double maximum, string expected)
        => Assert.Equal(expected, ProtoEditorWindow.NormalizeProtoActionProbabilityText(input, maximum));

    [Fact]
    public void ShadingTypes_AreCapitalizedForDisplayButCanonicalizedForXml()
    {
        Assert.Contains("Cocoon", ProtoConstants.KnownShadingTypeDisplayNames);
        Assert.Equal("Cocoon", ProtoConstants.GetShadingTypeDisplayName("cocoon"));
        Assert.Equal("cocoon", ProtoConstants.GetShadingTypeXmlValue("Cocoon"));
    }

    [Fact]
    public void AdditionalAttributePicker_UsesSharedOpenAndAttachedScrollBehavior()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("EnableDropdownAutoComplete(pickerAcb, selectAllOnFirstClick: false);", source, StringComparison.Ordinal);
        Assert.Contains("FreezeEditorScrollWhileDropDownIsOpen(pickerAcb, _editorScroll);", source, StringComparison.Ordinal);
        Assert.Contains("autoCompleteBox.IsDropDownOpen && sourceIsInsideEditor", source, StringComparison.Ordinal);
        Assert.Contains("new ColumnDefinitions(\"Auto, Auto, Auto\")", source, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(2, 0, 0, 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalAttributes_UseSpecializedAreaSortCastTargetAndChargedEditors()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("var areaSortModes = new[] { \"Radial\", \"Directional\" };", source, StringComparison.Ordinal);
        Assert.Contains("SetProtoActionSimpleFieldValue(state.Model, option.Tag, \"Radial\")", source, StringComparison.Ordinal);
        Assert.Contains("var initialTarget = string.IsNullOrWhiteSpace(value) ? \"Unit\" : value;", source, StringComparison.Ordinal);
        Assert.Contains("GetKnownUnitTypeNames()\n                    .Concat(GetAvailableTrainUnitNames())", source, StringComparison.Ordinal);
        Assert.Contains("state.ForcedVisibleFieldTags.Contains(\"charged\")", source, StringComparison.Ordinal);
        Assert.Contains("var usesFullChargedEditor = !IsBolsterActionType(currentActionType)", source, StringComparison.Ordinal);
        Assert.Contains(".Where(x => !x.Equals(\"charged\", StringComparison.OrdinalIgnoreCase))", source, StringComparison.Ordinal);
        Assert.Contains("SetChildValue(charged, \"chargedremove\", removeChargeEnabled ? \"true\" : \"\");", source, StringComparison.Ordinal);
        Assert.Contains("chargedModify?.Remove();", source, StringComparison.Ordinal);
        Assert.Contains("state.OptionalFieldsContainer.Children.Add(chargedControl);", source, StringComparison.Ordinal);
        Assert.Contains("RenderAdditionalGatheringTypeChips(state, gatheringTypeValues, state.OptionalFieldsContainer);", source, StringComparison.Ordinal);
        Assert.Contains("\"displaynameid\", \"Display Name Override\"", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void ChargedMerge_PreservesRepeatedProtoContainersAndPickerHidesNestedAttachment()
    {
        var inherited = new[]
        {
            XElement.Parse("<charged><chargedmodelattachment>inherited.xml</chargedmodelattachment></charged>")
        };
        var proto = new[]
        {
            XElement.Parse("<charged><chargedmodelattachment>success.xml</chargedmodelattachment><chargedmodelattachmentbone>vfx_top</chargedmodelattachmentbone></charged>"),
            XElement.Parse("<charged><chargedmodelattachment>fail.xml</chargedmodelattachment><chargedmodelattachmentbone>vfx_top</chargedmodelattachmentbone></charged>")
        };

        var merged = ProtoEditorWindow.MergeProtoActionAdditionalElements(inherited, proto);
        var charged = merged.Where(element => element.Name.LocalName.Equals("charged", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(2, charged.Count);
        Assert.Equal("success.xml", charged[0].Element("chargedmodelattachment")?.Value);
        Assert.Equal("fail.xml", charged[1].Element("chargedmodelattachment")?.Value);

        var source = ReadProtoEditorSource();
        var excludedStart = source.IndexOf("ProtoActionAttributePickerExcludedTags", StringComparison.Ordinal);
        var excludedEnd = source.IndexOf("CompactProtoActionAnimationFieldTags", excludedStart, StringComparison.Ordinal);
        Assert.True(excludedStart >= 0 && excludedEnd > excludedStart);
        Assert.Contains("\"chargedmodelattachment\"", source[excludedStart..excludedEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalAttributes_UseStrictCatalogsAndStructuredChipEditors()
    {
        var source = ReadProtoEditorSource();
        var gatheringTypes = ProtoActionMetadataCatalog.GetFieldDefinition("donotautogatherunlessgatheringtypes");
        var modifyAbstractType = ProtoActionMetadataCatalog.GetFieldDefinition("modifyabstracttype");

        Assert.Equal(ProtoActionFieldEditorKind.StructuredList, gatheringTypes.EditorKind);
        Assert.True(gatheringTypes.IsRepeatable);
        Assert.Equal("Modify Abstract Type", modifyAbstractType.Label);
        Assert.DoesNotContain(
            ProtoActionMetadataCatalog.GetKnownFieldDefinitions(),
            definition => definition.Tag.Equals("minworkrate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("RenderAdditionalStructuredValueChips(", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"modifydamagetargettype\"", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"modifyciv\"", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"modifyresourcesubtype\"", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionNumberBehavior", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalAttributes_UseRemovableCompactChipsAndOptionalRateAttributes()
    {
        var source = ReadProtoEditorSource();
        var chipRendererStart = source.IndexOf("private void RenderAdditionalStructuredValueChips(", StringComparison.Ordinal);
        var optionalRendererStart = source.IndexOf("private void RenderProtoActionOptionalFields(", chipRendererStart, StringComparison.Ordinal);
        Assert.True(chipRendererStart >= 0 && optionalRendererStart > chipRendererStart);
        var chipRenderer = source[chipRendererStart..optionalRendererStart];

        Assert.Contains("removeSectionButton", chipRenderer, StringComparison.Ordinal);
        Assert.Contains("picker.Width = 100;", chipRenderer, StringComparison.Ordinal);
        Assert.Contains("selectorRow.Children.Add(chips);", chipRenderer, StringComparison.Ordinal);
        Assert.Contains("state.CustomValues[\"additional.rof\"] = \"1\";", source, StringComparison.Ordinal);
        Assert.Contains("AddOptionalAttribute(\"resource\", \"Resource\", useResourceCombo: true);", source, StringComparison.Ordinal);
        Assert.Contains("AddOptionalAttribute(\"yield\", \"Yield\", useResourceCombo: false);", source, StringComparison.Ordinal);
        Assert.Contains("AddOptionalAttribute(\"overrideResource\", \"Override Resource\", useResourceCombo: true);", source, StringComparison.Ordinal);
        Assert.Contains("AddOptionalAttribute(\"inventoryRate\", \"Inventory Rate\", useResourceCombo: false);", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"restrictempowertype\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalAttributes_UseNestedContainedScalingAndUnitAnimationEditors()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadProtoActionMetadataSource();

        Assert.Contains("\"scalebycontainedunittype\", \"Scale By Contained Unit Type\", ProtoActionFieldEditorKind.StructuredList, true, [\"type\"]", metadata, StringComparison.Ordinal);
        Assert.Contains("SetScaleByContainedUnitTypeEntries", source, StringComparison.Ordinal);
        Assert.Contains("element.Name.LocalName.Equals(\"rate\"", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"sizeclassanim\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"Size Class:\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"Anim:\"", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"wateranim\"", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"splashvfxproto\"", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureUnitAnimationAutoComplete(splashEditor);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaleByContainedUnitType_WritesNestedRateElements()
    {
        var action = new ProtoAction();
        var entry = new ProtoActionStructuredFieldEntry { Value = "0.050000" };
        entry.Attributes["type"] = "Unit";

        ProtoEditorWindow.SetScaleByContainedUnitTypeEntries(action, [entry]);

        var wrapper = Assert.Single(action.AdditionalElements);
        Assert.Equal("scalebycontainedunittype", wrapper.Name.LocalName);
        var rate = Assert.Single(wrapper.Elements());
        Assert.Equal("rate", rate.Name.LocalName);
        Assert.Equal("Unit", rate.Attribute("type")?.Value);
        Assert.Equal("0.050000", rate.Value);

        ProtoEditorWindow.SetScaleByContainedUnitTypeEntries(action, []);
        Assert.Empty(action.AdditionalElements);
    }

    [Fact]
    public void AdditionalAttributes_ReuseComplexEditorsAndStrictUnitScopedReferences()
    {
        var source = ReadProtoEditorSource();

        Assert.Equal(ProtoActionFieldEditorKind.Number,
            ProtoActionMetadataCatalog.GetFieldDefinition("unintentionaldamagemultiplier").EditorKind);
        Assert.DoesNotContain("unintentionaldamagemultiplier", ProtoActionMetadataCatalog.GetKnownFlagTags(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ProtoActionFieldEditorKind.StructuredList,
            ProtoActionMetadataCatalog.GetFieldDefinition("empowerdata").EditorKind);
        Assert.Equal(ProtoActionFieldEditorKind.StructuredList,
            ProtoActionMetadataCatalog.GetFieldDefinition("stackcontrol").EditorKind);
        Assert.Contains("EmpowerSectionTags.Concat([\"stackcontrol\"])", source, StringComparison.Ordinal);
        Assert.Contains("state.ForcedVisibleFieldTags.Contains(\"stackcontrol\")", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureStrictSuggestionAutoComplete(editor, GetCurrentUnitProtoActionNames(), initialValue)", source, StringComparison.Ordinal);
        Assert.Contains("tag.Equals(\"conversionprotoid\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"Source:\"", source, StringComparison.Ordinal);
        Assert.Contains("? \"To:\"", source, StringComparison.Ordinal);
        Assert.Contains("\"activationtype\",\n        \"afteraction\",\n        \"chargedmodify\",\n        \"chargedmodelattachment\",\n        \"modifyamountbytier\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StackControlChildrenAreOnlyRenderedBySharedEditor_AndChargedModifyInitializationIsGuarded()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("=> ManagedStackControlChildTags.Contains(tag, StringComparer.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("var isInitializingFullChargedEditor = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (_isPopulating || isInitializingFullChargedEditor)", source, StringComparison.Ordinal);
        Assert.Contains("isInitializingFullChargedEditor = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (state.IsRefreshingMetadataPanels)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Hoplite", "Divine Blast", "STR_ACTION_HOPLITE_DIVINE_BLAST")]
    [InlineData("Camel Rider", "  Sacred-Aura  ", "STR_ACTION_CAMEL_RIDER_SACRED_AURA")]
    public void ProtoActionDisplayNameOverride_UsesUnitAndActionStringIds(string unitName, string actionName, string expected)
        => Assert.Equal(expected, ProtoEditorWindow.BuildProtoActionDisplayNameStringId(unitName, actionName));

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")))
            .ReplaceLineEndings("\n");

    private static string ReadProtoActionMetadataSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Classes", "ProtoActionMetadata.cs")))
            .ReplaceLineEndings("\n");

    private static string ReadAttachmentEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Controls", "AttachmentEditor.cs")))
            .ReplaceLineEndings("\n");
}
