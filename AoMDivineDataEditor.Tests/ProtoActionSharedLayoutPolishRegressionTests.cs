using System;
using System.IO;
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
        Assert.Contains("CreateAttachmentEditor(\"modelattachment\", modelAttachmentValue, 200)", source, StringComparison.Ordinal);
        Assert.Contains("CreateAttachmentEditor(\"modelattachmentbone\", modelAttachmentBoneValue, 100)", source, StringComparison.Ordinal);
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
    public void BolsterAndChargedModifiers_UseStrictRequestedNumericAndDamageTypeControls()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Content = \"Max stacks on target\"", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionIntegerBehavior(modifyAmountTextBox)", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Animation:\", animEditor", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Projectile:\", projectileEditor", source, StringComparison.Ordinal);
        Assert.Contains("var paramCb = new ComboBox", source, StringComparison.Ordinal);
        Assert.Contains("\"ArmorSpecific\" => [\"Hack\", \"Pierce\", \"Crush\"]", source, StringComparison.Ordinal);
        Assert.Contains("\"DamageSpecific\" => [\"Hack\", \"Pierce\", \"Crush\", \"Divine\"]", source, StringComparison.Ordinal);
        Assert.Contains("AttachProtoActionDecimalBehavior(valueTb, () => true)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Charged Modifier\"", source, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")));
}
