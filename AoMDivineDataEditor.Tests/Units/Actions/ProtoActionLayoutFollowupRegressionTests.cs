using System;
using System.IO;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoActionLayoutFollowupRegressionTests
{
    [Fact]
    public void AreaRestrictVfx_UsesTheSharedCompactVisualFieldWidth()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("\"chargedmodifyvfx\",", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProtoActionFieldWidth(chargedEditor, \"chargedmodifyvfx\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreRangeLayout_PutsMaxRangeFirstWhenRofAndMinRangeAreHidden()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("if (showMaxRange && !showRof && !showCoreMinRange)", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(state.MaxRangeLabel, 0);", source, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(state.MaxRangeTb, 1);", source, StringComparison.Ordinal);
        Assert.Contains("showMaxRange && !showRof && !showCoreMinRange ? 0 : 18", source, StringComparison.Ordinal);
        Assert.Contains("var showRof = state.RofLabel.IsVisible;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistAttack_KeepsOptionsBelowTheSharedRangeRow()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("Tag = \"assistattack.options\"", source, StringComparison.Ordinal);
        Assert.Contains("AddAssistOption(\"modifytargetlimit\", \"Max Targets\");", source, StringComparison.Ordinal);
        Assert.Contains("AddAssistOption(\"modifymultiplier\", \"Lifesteal\");", source, StringComparison.Ordinal);
        Assert.Contains("coreIndex + 2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumn(state.MaxRangeLabel, 2);\n            Grid.SetColumn(state.MaxRangeTb, 3);\n            var nextColumn", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProtoAction_TypeChangesRemoveOnlyEditorInjectedDefaultFlags()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("DefaultInjectedFlagTags", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var flagTag in state.DefaultInjectedFlagTags.ToList())", source, StringComparison.Ordinal);
        Assert.Contains("ProtoXmlHandler.SetProtoActionSimpleFieldValue(state.Model, flagTag, \"\");", source, StringComparison.Ordinal);
        Assert.Contains("state.DefaultInjectedFlagTags.Add(flagTag);", source, StringComparison.Ordinal);
        Assert.Contains("EnsureProtoActionDefaultFlags(state, selectedType ?? \"\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionReordering_UsesTheBodyContainerSoControlsAreNotReparentedAcrossPanels()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("var container = state.BodyContainer;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (state.Container is not Panel container)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProtoAction_HidesItsBodyUntilAValidTypeIsSelected()
    {
        var source = ReadProtoEditorSource();

        Assert.Contains("state.BodyContainer.IsVisible = hasSelectedType;", source, StringComparison.Ordinal);
        Assert.Contains("state.ActiveLabel.IsVisible = hasSelectedType;", source, StringComparison.Ordinal);
        Assert.Contains("if (!hasSelectedType)", source, StringComparison.Ordinal);
    }

    private static string ReadProtoEditorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Windows", "ProtoEditorWindow.axaml.cs")))
            .ReplaceLineEndings("\n");
}
