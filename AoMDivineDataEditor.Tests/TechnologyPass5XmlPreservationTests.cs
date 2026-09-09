using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyPass5XmlPreservationTests
{
    [Fact]
    public void MixedEffectShapesAreEnumeratedInDocumentOrderWithoutMutation()
    {
        var technology = XElement.Parse("""
            <tech name="Mixed">
              <effect type="First" />
              <unknown />
              <effects>
                <effect type="Second" />
                <future value="preserved" />
                <effect type="Third" />
              </effects>
              <effect type="Fourth" />
            </tech>
            """, LoadOptions.PreserveWhitespace);
        var before = technology.ToString(SaveOptions.DisableFormatting);

        var effects = TechnologyXmlShapePolicy.EnumerateEffectsInDocumentOrder(technology);

        Assert.Equal(["First", "Second", "Third", "Fourth"],
            effects.Select(effect => (string?)effect.Attribute("type")));
        Assert.Equal(before, technology.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void OnlyChangedOrNewEffectsAreSelectedForNormalization()
    {
        var saved = XElement.Parse("""
            <tech name="T">
              <effect type="Data" custom="keep-position" amount="1"><future /></effect>
              <effect type="Sound" file="a" />
            </tech>
            """);
        var current = new XElement(saved);
        current.AddFirst(new XElement("unknown", "preserved"));

        Assert.Empty(TechnologyXmlShapePolicy.FindChangedEffects(current, saved));

        current.Elements("effect").Last().SetAttributeValue("file", "b");
        current.Add(new XElement("effect", new XAttribute("type", "FutureType")));
        var changed = TechnologyXmlShapePolicy.FindChangedEffects(current, saved);

        Assert.Equal(2, changed.Count);
        Assert.Equal("Sound", (string?)changed[0].Attribute("type"));
        Assert.Equal("FutureType", (string?)changed[1].Attribute("type"));
    }

    [Fact]
    public void UnknownNestedAndContainerContentIsIdentifiedForReadOnlyDisplay()
    {
        var technology = XElement.Parse("""
            <tech name="T">
              <known><nested /></known>
              <future><payload /></future>
              <effects><futureEffectMetadata /><effect type="Data" /></effects>
            </tech>
            """);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known", "effects" };

        var topLevel = TechnologyXmlShapePolicy.FindUnhandledNestedTechnologyChildren(technology, owned);
        var container = TechnologyXmlShapePolicy.FindNonEffectContainerChildren(technology);

        Assert.Equal("future", Assert.Single(topLevel).Name.LocalName);
        Assert.Equal("futureEffectMetadata", Assert.Single(container).Name.LocalName);
    }

    [Fact]
    public void SaveAndPreviewNormalizeChangedEffectsWithoutReorderingTechnologyChildren()
    {
        var source = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("NormalizeChangedEffectAttributeOrder(technology)", source, StringComparison.Ordinal);
        Assert.Contains("TechnologyXmlShapePolicy.FindChangedEffects(previewTech, savedTechnology)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeTechnologyChildOrder(technology);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeTechnologyChildOrder(previewTech);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportOnlyFlagRemainsStructuredButIsNotCreatable()
    {
        var source = ReadSource("Windows", "TechnologyEditorView.axaml.cs");

        Assert.Contains("\"ProtoUnitFlag\", \"Flag\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Flag", TechnologyEffectCatalogInventory.CreatableDataSubtypes);
        Assert.True(TechnologyEffectCatalogInventory.IsStructuredDataSubtype("Flag"));
    }

    private static string ReadSource(params string[] relativePath)
        => File.ReadAllText(Path.Combine([FindProjectRoot(), .. relativePath]));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AoMDivineDataEditor.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
