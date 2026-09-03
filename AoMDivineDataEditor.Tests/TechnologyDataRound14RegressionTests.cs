using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound14RegressionTests
{
    [Fact]
    public void ImportedOverrideRelativity_IsPreservedWithoutJoiningStandardOptions()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="Resource" amount="2" relativity="Override" resource="Food">
              <target type="Player" />
            </effect>
            """);

        Assert.False(TechnologyDataEffectRules.NormalizeResourceEffect(effect));
        Assert.Equal("Override", (string?)effect.Attribute("relativity"));
        Assert.DoesNotContain("Override", TechnologyDataEffectRules.ResourceRelativityDisplayOptions);
    }

    [Fact]
    public void WorkRateEditors_OptIntoOverrideAndSpecificUsesSeparateResourceDropdown()
    {
        var code = NormalizeNewlines(ReadTechnologyEditor());

        Assert.Contains("subtype.Equals(\"BuildingWorkRate\"", code, StringComparison.Ordinal);
        Assert.Contains("AddSimpleUnitAmountDataEffectEditor(effect, content, allowOverride: true)", code, StringComparison.Ordinal);
        Assert.Contains("private void AddMinWorkRateDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("bool allowOverride = true", code, StringComparison.Ordinal);
        Assert.Contains("AddDataRelativityAndAmountEditors(effect, row, allowOverride);", code, StringComparison.Ordinal);
        Assert.Contains("private void AddWorkRateSpecificDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"For\", CreateStrictEffectSelector(\n            _prereqUnitNames", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Resource\", CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);
        Assert.Contains("if (allowOverride || currentRelativity.Equals(\"Override\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Override\" => \"Override\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoAttackType_UsesShortTargetLabel()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("CreateLabeledEffectSegment(\"Can auto attack\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLabeledEffectSegment(\"New auto attack target\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLabeledEffectSegment(\"Add as valid auto attack target\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkRateAll_UsesActionAmountLayoutWithOverride()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("subtype.Equals(\"WorkRateAll\"", code, StringComparison.Ordinal);
        Assert.Contains("AddActionUnitAmountDataEffectEditor(effect, content, allowOverride: true)", code, StringComparison.Ordinal);
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
