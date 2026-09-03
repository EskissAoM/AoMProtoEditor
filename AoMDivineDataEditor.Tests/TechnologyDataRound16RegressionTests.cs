using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound16RegressionTests
{
    [Fact]
    public void EmpowerAndPlacementCorrectionsUseRequestedDefaultsAndCatalogs()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("private void AddEmpowerEnableDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, \"Empower\")", code, StringComparison.Ordinal);
        Assert.Contains("\"Assign rules of\", \"unittype\", _protoUnitNames", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VeterancyEditorsUseRankModifyAndRankTypeAttributes()
    {
        var code = NormalizeNewlines(ReadTechnologyEditor());

        Assert.Contains("AddSetVeterancyRankActiveDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateUnsignedIntegerEffectBox(effect, \"rank\"", code, StringComparison.Ordinal);
        Assert.Contains("AddInitialVeterancyRankDataEffectEditor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFixedAssignRelativityCombo", code, StringComparison.Ordinal);
        Assert.Contains("\"Start at rank\",\n            CreateUnsignedIntegerEffectBox(effect, \"amount\"", code, StringComparison.Ordinal);

        Assert.Contains("AddVeterancyBonusDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Bonus\", CreateStrictEffectSelector", code, StringComparison.Ordinal);
        Assert.Contains("currentModifyType is \"DamageSpecific\" or \"ArmorSpecific\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateRequiredDataTypeCombo(effect, includeDivine: currentModifyType == \"DamageSpecific\")", code, StringComparison.Ordinal);

        Assert.Contains("AddVeterancyRankAddDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("new[] { \"Attacks\", \"Kills\", \"Damage\" }", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"rankType\", selected)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SnareSpeedStackTacticAndTurnRateUseSharedStructuredControls()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("\"VolleyMode\", \"Snare\"", code, StringComparison.Ordinal);
        Assert.Contains("\"GatherRateMultiplier\", \"TurnRate\"", code, StringComparison.Ordinal);
        Assert.Contains("AddSpeedModifierDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Tactic\", CreateTacticNameEditor(effect)", code, StringComparison.Ordinal);
        Assert.Contains("AddStackControlDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, \"StackControl\")", code, StringComparison.Ordinal);
        Assert.Contains("AddTacticEnableDataEffectEditor", code, StringComparison.Ordinal);
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
