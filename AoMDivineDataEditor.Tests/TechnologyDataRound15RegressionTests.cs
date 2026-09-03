using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound15RegressionTests
{
    [Fact]
    public void SimpleAndContainingAmountFamiliesIncludeRequestedSubtypes()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("\"ContainedHitpointBonus\", \"GatherRateMultiplier\"", code, StringComparison.Ordinal);
        Assert.Contains("ContainingUnitAmountDataSubtypes", code, StringComparison.Ordinal);
        Assert.Contains("\"ContainedHitpointBonusUnitType\", \"GarrisonBonusDamage\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Containing\", CreateStrictEffectSelector", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BoostAndEmpowerEditorsUseRequestedStructuredControls()
    {
        var code = NormalizeNewlines(ReadTechnologyEditor());

        Assert.Contains("AddBoostRadiusDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"targetType\",\n            _prereqUnitNames", code, StringComparison.Ordinal);

        Assert.Contains("AddEmpowerAreaDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, \"Empower\")", code, StringComparison.Ordinal);
        Assert.Contains("AddEmpowerEnableDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateEnableDisableAmountCombo(effect)", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Player affected\", CreateEmpowerPlayerTypeCombo(effect)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformPlacementGatherAndReturnRateEditorsUseRequestedSemantics()
    {
        var code = NormalizeNewlines(ReadTechnologyEditor());

        Assert.Contains("AddFixedUnitReferenceDataEffectEditor(effect, content, \"Transform to\", \"unittype\", _protoUnitNames)", code, StringComparison.Ordinal);
        Assert.Contains("AddFixedUnitReferenceDataEffectEditor(effect, content, \"Assign rules of\", \"unittype\", _protoUnitNames)", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"amount\", \"1\")", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", code, StringComparison.Ordinal);

        Assert.Contains("AddGatherResourceOverrideDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, \"Gather\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"For\", CreateStrictEffectSelector(\n            _prereqUnitNames", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Resource\", CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);

        Assert.Contains("\"FakeConversion\", \"ResourceReturnRateTotalCost\"", code, StringComparison.Ordinal);
    }

    private static string ReadTechnologyEditor()
    {
        var root = FindProjectRoot();
        return File.ReadAllText(Path.Combine(root, "Windows", "TechnologyEditorView.axaml.cs"));
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

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
