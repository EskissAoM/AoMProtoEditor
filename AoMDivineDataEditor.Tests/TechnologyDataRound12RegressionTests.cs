using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound12RegressionTests
{
    [Fact]
    public void MaxResource_IsAbsoluteAndTargetsOnlyPlayer()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="MaxResource" amount="25" relativity="Percent" resource="Favor">
              <target type="ProtoUnit">Villager</target>
              <target type="UnitType">Hero</target>
            </effect>
            """);

        Assert.True(TechnologyDataEffectRules.NormalizeMaxResourceEffect(effect));
        Assert.Equal("25", (string?)effect.Attribute("amount"));
        Assert.Equal("Absolute", (string?)effect.Attribute("relativity"));
        Assert.Equal("Favor", (string?)effect.Attribute("resource"));
        var target = Assert.Single(effect.Elements("target"));
        Assert.Equal("Player", (string?)target.Attribute("type"));
        Assert.True(target.IsEmpty);
    }

    [Theory]
    [InlineData("Absolute")]
    [InlineData("Percent")]
    [InlineData("BasePercent")]
    [InlineData("Assign")]
    public void PopulationCap_SupportsEveryStandardRelativity(string relativity)
    {
        var effect = new XElement("effect",
            new XAttribute("type", "Data"),
            new XAttribute("subtype", "PopulationCap"),
            new XAttribute("amount", "10"),
            new XAttribute("relativity", relativity),
            new XElement("target", new XAttribute("type", "Player")));

        Assert.False(TechnologyDataEffectRules.NormalizePopulationCapEffect(effect));
        Assert.Equal(relativity, (string?)effect.Attribute("relativity"));
    }

    [Fact]
    public void SetCivilization_UsesFixedAssignSemanticsAndPlayerTarget()
    {
        var effect = XElement.Parse("""
            <effect type="Data" subtype="SetCivilization" civ="Zeus" amount="3" relativity="Percent">
              <target type="ProtoUnit">Villager</target>
            </effect>
            """);

        Assert.True(TechnologyDataEffectRules.NormalizeSetCivilizationEffect(effect));
        Assert.Equal("Zeus", (string?)effect.Attribute("civ"));
        Assert.Equal("1", (string?)effect.Attribute("amount"));
        Assert.Equal("Assign", (string?)effect.Attribute("relativity"));
        Assert.Equal("Player", (string?)Assert.Single(effect.Elements("target")).Attribute("type"));
    }

    [Fact]
    public void TechnologyEditor_ProvidesDedicatedRound12Editors()
    {
        var code = ReadTechnologyEditor();

        Assert.Contains("AddSharedBuildLimitUnitDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("_protoUnitNames,\n            GetCaseInsensitiveAttribute(effect, \"unitType\")", NormalizeNewlines(code), StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Shared with\"", code, StringComparison.Ordinal);

        Assert.Contains("AddMaxResourceDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("\"Capped at initial resource +\"", code, StringComparison.Ordinal);
        Assert.Contains("CreateResourceCombo(effect, \"resource\")", code, StringComparison.Ordinal);

        Assert.Contains("AddPopulationCapDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("AddSetCivilizationDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("private ComboBox CreateForcedPlayerTargetCombo", code, StringComparison.Ordinal);
        Assert.Contains("SelectedItem = \"Player\",\n            IsEnabled = false", NormalizeNewlines(code), StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Set to\", CreateStrictEffectSelector(\n            _majorGodNames", NormalizeNewlines(code), StringComparison.Ordinal);
        Assert.Contains("GetCaseInsensitiveAttribute(effect, \"civ\")", code, StringComparison.Ordinal);
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
