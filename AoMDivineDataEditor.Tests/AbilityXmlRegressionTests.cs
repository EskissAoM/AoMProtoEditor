using System.Xml.Linq;
using AoMDivineDataEditor.Windows;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AbilityXmlRegressionTests
{
    [Theory]
    [InlineData("AbilityCaladriaBurstHeal", false, "STR_ABILITY_ABILITYCALADRIABURSTHEAL")]
    [InlineData("AbilityCaladriaBurstHeal", true, "STR_ABILITY_ABILITYCALADRIABURSTHEAL_LR")]
    [InlineData("A-B  C", false, "STR_ABILITY_A_B_C")]
    public void BuildAbilityStringId_IsStableAndSanitized(string abilityName, bool rollover, string expected)
    {
        Assert.Equal(expected, ProtoEditorWindow.BuildAbilityStringId(abilityName, rollover));
    }

    [Fact]
    public void LoadAbilityXmlDocumentForUpdate_MissingFileCreatesRequestedRoot()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "powers_mods.xml");

        var document = ProtoEditorWindow.LoadAbilityXmlDocumentForUpdate(path, "powersmod");

        Assert.Equal("powersmod", document.Root?.Name.LocalName);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadAbilityXmlDocumentForUpdate_MalformedExistingFileThrowsWithoutReplacingIt()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "powers_mods.xml");
        const string malformed = "<powersmod><power name=\"Broken\"></powersmod>";
        File.WriteAllText(path, malformed);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ProtoEditorWindow.LoadAbilityXmlDocumentForUpdate(path, "powersmod"));

        Assert.Contains("could not be read safely", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(malformed, File.ReadAllText(path));
    }

    [Fact]
    public void SaveAbilityXmlDocument_WritesIndentedXmlWithoutDeclaration()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "powers_mods.xml");
        var document = new XDocument(new XElement("powersmod",
            new XElement("power", new XAttribute("name", "Test"), new XAttribute("type", "UnitAction"),
                new XElement("unitaction", "HandAttack"))));

        ProtoEditorWindow.SaveAbilityXmlDocument(document, path);
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("<?xml", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.NewLine, text);
        Assert.Equal("powersmod", XDocument.Load(path).Root?.Name.LocalName);
    }

    [Fact]
    public void MergeUnknownPlacementAttributes_PreservesUnknownNameCasingAndValue()
    {
        var source = XElement.Parse("""
            <power name="Source" type="UnitAction">
              <placement enemy="" FutureAttribute="42" MyCustomFlag="abc">Unit</placement>
            </power>
            """);
        var target = XElement.Parse("""
            <power name="Target" type="UnitAction">
              <placement includeally="">Unit</placement>
            </power>
            """);

        ProtoEditorWindow.MergeUnknownPlacementAttributes(source, target);

        var placement = Assert.Single(target.Elements("placement"));
        Assert.Equal("42", placement.Attribute("FutureAttribute")?.Value);
        Assert.Equal("abc", placement.Attribute("MyCustomFlag")?.Value);
        Assert.NotNull(placement.Attribute("includeally"));
        Assert.Null(placement.Attribute("enemy")); // managed attributes come from the editor target, not the source.
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", null, true)]
    [InlineData("Circle", null, false)]
    [InlineData("Arrow", "", false)]
    [InlineData("Cone", "15", true)]
    public void RangeIndicator_RequiresRangeOnlyWhenIndicatorIsPresent(string? indicator, string? range, bool expected)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (range != null)
            attributes["range"] = range;

        Assert.Equal(expected, ProtoEditorWindow.AbilityRangeIndicatorHasRequiredRange(indicator, attributes));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AoMDivineDataEditor.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
