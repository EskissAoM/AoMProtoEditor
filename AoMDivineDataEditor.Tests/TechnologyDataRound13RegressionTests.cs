using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDataRound13RegressionTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ProtoActionAdd_NormalizesOmissionBasedAddToTacticsDefault(string? storedValue, bool expectedChecked)
    {
        var effect = new XElement("effect",
            new XAttribute("amount", "9"),
            new XAttribute("relativity", "Percent"));
        if (storedValue != null) effect.SetAttributeValue("addToTactics", storedValue);

        TechnologyDataEffectRules.NormalizeProtoActionAddEffect(effect);

        Assert.Equal("1", (string?)effect.Attribute("amount"));
        Assert.Equal("Assign", (string?)effect.Attribute("relativity"));
        var checkedAfterNormalization = !string.Equals(
            effect.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("addToTactics", StringComparison.OrdinalIgnoreCase))?.Value,
            "0",
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedChecked, checkedAfterNormalization);
        Assert.Equal(expectedChecked ? null : "0", (string?)effect.Attribute("addToTactics"));
    }

    [Fact]
    public void TechnologyEditor_ProvidesRequestedRound13Layouts()
    {
        var code = NormalizeNewlines(ReadSource("Windows", "TechnologyEditorView.axaml.cs"));

        Assert.Contains("\"DamageCap\", \"AnimationRate\"", code, StringComparison.Ordinal);

        Assert.Contains("AddEmpowerModifyDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultDataAction(effect, \"Empower\")", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Target\", CreateStrictEffectSelector(\n            _prereqUnitNames", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Modify Type\"", code, StringComparison.Ordinal);
        Assert.Contains("var options = new[] { \"Self\", \"Enemy\", \"Gaia\" }", code, StringComparison.Ordinal);

        Assert.Contains("AddProtoActionAddDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Grants\", CreateStrictEffectSelector(\n            _protoActionNames", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"From\", CreateStrictEffectSelector(\n            _protoUnitNames", code, StringComparison.Ordinal);
        Assert.Contains("Content = \"Add to tactics\"", code, StringComparison.Ordinal);
        Assert.Contains("SetCaseInsensitiveAttribute(effect, \"addToTactics\", \"0\")", code, StringComparison.Ordinal);
        Assert.Contains("RemoveCaseInsensitiveAttribute(effect, \"addToTactics\")", code, StringComparison.Ordinal);

        Assert.Contains("AddAutoAttackTypeDataEffectEditor", code, StringComparison.Ordinal);
        Assert.Contains("private Control CreateTacticNameEditor", code, StringComparison.Ordinal);
        Assert.Contains("return _tacticNames.Count > 0", code, StringComparison.Ordinal);
        Assert.Contains("CreateFreeTextEffectAttributeBox(effect, \"tactic\", width)", code, StringComparison.Ordinal);
        Assert.Contains("CreateLabeledEffectSegment(\"Can auto attack\", CreateStrictEffectSelector(\n            _prereqUnitNames", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtoEditor_ReusesTacticsSourcesForInnerTacticNameCatalog()
    {
        var code = ReadSource("Windows", "ProtoEditorWindow.axaml.cs");

        Assert.Contains("GetTechnologyTacticDefinitionNames()", code, StringComparison.Ordinal);
        Assert.Contains("ReadBarXmbXml(entry, stream)", code, StringComparison.Ordinal);
        Assert.Contains("tactic.Nodes().OfType<XText>()", code, StringComparison.Ordinal);
        Assert.Contains("AddLooseTactics(ResolveBaseGameplayDirectory())", code, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindProjectRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
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
