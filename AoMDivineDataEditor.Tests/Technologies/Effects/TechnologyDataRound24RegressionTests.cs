using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound24RegressionTests
{
    [Fact]
    public void RequestedPlayerTogglesShareFixedAssignHandler()
    {
        var code = ReadTechnologyEditor();

        foreach (var subtype in new[] { "BuildingChainActive", "CombatXP", "RevealAllyUI", "RevealEnemyUI" })
            Assert.Contains($"subtype.Equals(\"{subtype}\"", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerEnableDisableDataEffectEditor(effect, content)", code, StringComparison.Ordinal);
        Assert.Contains("EnsureExactDataAttribute(effect, \"relativity\", \"Assign\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateEnableDisableAmountCombo(effect)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GodPowerAndTributeAmountsUseRequestedOverrideAvailability()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("subtype.Equals(\"GodPowerCost\"", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"GodPowerCostFactor\"", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerRelativityAmountDataEffectEditor(effect, content, allowOverride: true)", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"GodPowerROF\"", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"GodPowerROFFactor\"", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"TributePenalty\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentShiftsUsesRestrictedOverrideRelativity()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddRestrictedPlayerRelativityAmountDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("effect, content, [\"Absolute\", \"Assign\", \"Override\"]", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceByKbStatReusesPrerequisiteCatalogAndParameterRules()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddResourceByKbStatDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Resource\", CreateResourceCombo(effect, \"resource\"), leftSpacing: 8));\n        AddDataRelativityAndAmountEditors(effect, row);", code.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("var currentUsesResource = KbStatsUsingResourceParameter.Contains(currentStat)", code, StringComparison.Ordinal);
        Assert.Contains("KbStatNames,", code, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"kbparamresource\")", code, StringComparison.Ordinal);
        Assert.Contains("row, effect, \"Resource cap\", \"resourcecap\", \"1\"", code, StringComparison.Ordinal);
        Assert.Contains("ProtoUnitNumericKind.PositiveInteger, requirePositive: true", code, StringComparison.Ordinal);
    }

    private static string ReadTechnologyEditor()
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Windows", "TechnologyEditorView.axaml.cs"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AoMDivineDataEditor repository root.");
    }
}
