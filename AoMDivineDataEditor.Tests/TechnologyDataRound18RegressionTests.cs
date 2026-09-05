using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound18RegressionTests
{
    [Fact]
    public void PlayerTargetNormalizerKeepsEffectAttributesAndForcesEmptyPlayerTarget()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="TimeShiftingCost" unittype="Villager" amount="2" relativity="Percent">
              <target type="ProtoUnit">Villager</target>
            </effect>
            """);

        Assert.True(TechnologyDataEffectRules.NormalizePlayerTargetEffect(effect));
        Assert.Equal("Villager", (string?)effect.Attribute("unittype"));
        var target = Assert.Single(effect.Elements("target"));
        Assert.Equal("Player", (string?)target.Attribute("type"));
        Assert.True(target.IsEmpty);
    }

    [Fact]
    public void PlayerAmountFamiliesUseForcedPlayerAndRequestedExtraSelectors()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("subtype.Equals(\"RepairCostFactor\"", code, StringComparison.Ordinal);
        Assert.Contains("subtype.Equals(\"AutoGatherBonus\"", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerAmountDataEffectEditor(effect, content, includeResource: true)", code, StringComparison.Ordinal);
        Assert.Contains("AddPlayerAmountDataEffectEditor(effect, content, includeUnitType: true)", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Unit\", CreateStrictEffectSelector", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Resource\", CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SetAgeMarketAndMarketResetUseDedicatedPlayerLayouts()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddSetAgeDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("[\"Archaic\"] = \"1\"", code, StringComparison.Ordinal);
        Assert.Contains("[\"Wonder\"] = \"5\"", code, StringComparison.Ordinal);
        Assert.Contains("AddMarketDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"BuyFactor\", \"SellFactor\", \"BuyDelta\", \"SellDelta\", \"BuyFactorSpecific\", \"SellFactorSpecific\"", code, StringComparison.Ordinal);
        Assert.Contains("AddMarketResetDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Reset rate\", resetRate", code, StringComparison.Ordinal);
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
