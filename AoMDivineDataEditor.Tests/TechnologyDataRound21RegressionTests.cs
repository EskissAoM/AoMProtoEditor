using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound21RegressionTests
{
    [Fact]
    public void GoalSubtypesUseCanonicalAttributesAndFixedLayouts()
    {
        var code = ReadTechnologyEditor();
        var normalized = code.Replace("\r\n", "\n");

        Assert.Contains("AddGoalDataEffectEditor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureFixedDataAttribute(effect, \"goalname\"", code, StringComparison.Ordinal);
        Assert.Contains("\"goaltype\", [\"Damage\", \"Resource\", \"DeathCount\"]", code, StringComparison.Ordinal);
        Assert.Contains("\"rewardtrackingtype\", [\"Single\", \"PerPossibleReward\"]", code, StringComparison.Ordinal);
        Assert.Contains("AddGoalContributorDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("private void AddGoalContributorDataEffectEditor(XElement effect, StackPanel content)\n    {\n        EnsureExactDataAttribute(effect, \"relativity\", \"Assign\");", normalized, StringComparison.Ordinal);
        Assert.Contains("\"contributortype\"", code, StringComparison.Ordinal);
        Assert.Contains("\"contributorid\"", code, StringComparison.Ordinal);
        Assert.Contains("AddGoalRewardExclusionDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("AddSetGoalActiveDataEffectEditor", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BountySubtypesUseConditionalUnitResourceAndAttackerAttributes()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddBountyResourceEarningDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("[\"Damage\", \"Destroy\"]", code, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"resourcetype\")", code, StringComparison.Ordinal);
        Assert.Contains("\"Bonus for\", \"Bonus for\", \"attackertype\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerCostUsesProtoPowerAndOffersOverride()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("subtype.Equals(\"PowerCost\"", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerPowerAmountDataEffectEditor(effect, content, allowOverride: true)", code, StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"protopower\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalNameEditorFiltersToInternalNameCharacters()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("CreateInternalNameEffectAttributeBox", code, StringComparison.Ordinal);
        Assert.Contains("Where(InternalNamePolicy.IsAllowedCharacter)", code, StringComparison.Ordinal);
        Assert.Contains("if (filtered.Length == 0) RemoveCaseInsensitiveAttribute(effect, attributeName)", code, StringComparison.Ordinal);
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
