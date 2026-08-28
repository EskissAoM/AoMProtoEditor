using System;
using System.IO;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AbductActionPolishRegressionTests
{
    [Fact]
    public void Abduct_UsesOptionalMinRangeDurationSplashAndAnimationFields()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("IsAbductActionType(actionType)", source, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledFieldGroup(\"Duration (ms):\", durationEditor)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Splash VFX Proto\"", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureUnitAnimationAutoComplete(splashEditor);", source, StringComparison.Ordinal);
        Assert.Contains("AddAnimationField(\"walkanim\", \"Walk anim\", \"Walk anim\");", source, StringComparison.Ordinal);
        Assert.Contains("AddAnimationField(\"idleanim\", \"Idle anim\", \"Idle anim\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Abduct_CreatesAndLocksAbductDropCompanion()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Name = \"AbductDrop\"", source, StringComparison.Ordinal);
        Assert.Contains("Type = \"Inline\"", source, StringComparison.Ordinal);
        Assert.Contains("SetProtoActionSimpleFieldValue(companionAction, \"isabductdrop\", \"1\")", source, StringComparison.Ordinal);
        Assert.Contains("companion.RemoveButton.IsVisible = false;", source, StringComparison.Ordinal);
        Assert.Contains("state.AbductDropCompanion", source, StringComparison.Ordinal);
        Assert.Contains("pw.IsLinkedAbductDropAction", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AbductPairing_UsesSharedProtoActionEditorIncludingTacticsMode()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("_protoActionHostAdapter.LoadActions(unit)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureLinkedCompanionsForAction", source, StringComparison.Ordinal);
        Assert.Contains("EnsureAbductDropCompanionForAction(primary);", source, StringComparison.Ordinal);
        Assert.Contains("standalone Tactics Manager", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnHitEffect_UsesGenericHiddenByDefaultAdditionalAttributeRule()
    {
        var source = ReadProtoEditorSource();
        var metadata = ReadMetadataSource();

        Assert.Contains("IsDeferredComplexProtoActionAttributeAvailableFromPicker", source, StringComparison.Ordinal);
        Assert.Contains("HiddenByDefaultTags", source, StringComparison.Ordinal);
        Assert.Contains("options.Add((\"onhiteffect\", ProtoActionMetadataCatalog.GetFieldDefinition(\"onhiteffect\").Label));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (IsAbductActionType(actionType) &&", source);
        Assert.Contains("HiddenByDefaultTags: new HashSet<string>([\"damagebonus\", \"onhiteffect\"]", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Abduct_DoesNotFallThroughToBolsterCustomLayout()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("else if (IsBolsterActionType(actionType))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("else\r\n            {\r\n                state.CoreFieldsGrid.IsVisible = false;\r\n                state.MaxRangeLabel.IsVisible = false;\r\n                state.MaxRangeTb.IsVisible = false;\r\n\r\n                var animEditor = CreateSimpleEditor(\"anim\");\r\n                if (animEditor is AutoCompleteBox animAcb && string.IsNullOrWhiteSpace(animAcb.Text))\r\n                    animAcb.Text = \"Bolster\";", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Abduct_UsesAttackLikeOptionalMinRangeLayout()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("IsLinearAreaAttackActionType(actionType) || IsAbductActionType(actionType)", source, StringComparison.Ordinal);
        Assert.Contains("Content = \"Min Range\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreMinRange_IsNotOfferedAgainAsAdditionalAttributeAndWinsSerialization()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("IsManagedCoreMinRangeFieldTag(actionType, x.Tag)", source, StringComparison.Ordinal);
        Assert.Contains("Apply it after generic additional fields", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoActionAnimationFields_UseCompactTwoHundredPixelWidth()
    {
        var source = ReadProtoEditorSource();

        foreach (var tag in new[] { "walkanim", "idleanim", "anim", "typedanim", "landanim", "reloadanim", "wateranim", "sizeclassanim" })
            Assert.Contains("\"" + tag + "\"", source, StringComparison.Ordinal);
        Assert.Contains("const double animationFieldWidth = 200;", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProtoActionFieldWidth(editor, tag);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_HidesOnHitEffectByDefault()
    {
        var metadata = ReadMetadataSource();

        Assert.Contains("[\"Inline\"] = new ProtoActionTypeEditorProfile", metadata, StringComparison.Ordinal);
        Assert.Contains("[\"rof\", \"maxrange\", \"damage\", \"damagebonus\", \"onhiteffect\"]", metadata, StringComparison.Ordinal);
    }


    [Fact]
    public void AddProtoActionButton_HasNoLeadingPlus()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Content = \"Add Proto Action\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"+ Add Proto Action\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoActionTargetAndFlagChips_UseTheSameSharedChipFormat()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("return EditorChipService.CreateBlueChip(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("chip.Margin = new Thickness(4, 0, 0, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("targetFlagChip.Margin = new Thickness(4, 0, 0, 0);", source, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")))
            .ReplaceLineEndings("\n");

    private static string ReadMetadataSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Classes", "ProtoActionMetadata.cs")))
            .ReplaceLineEndings("\n");
}
