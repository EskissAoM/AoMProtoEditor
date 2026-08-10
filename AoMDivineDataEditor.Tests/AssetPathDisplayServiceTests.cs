using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AssetPathDisplayServiceTests
{
    [Theory]
    [InlineData(@"resources\greek\units\arcus.xml", "arcus.xml")]
    [InlineData(@"atlantean\units\automaton.xml", "automaton.xml")]
    public void CreateSuggestions_UsesCompactFilenameForUniquePaths(string fullPath, string display)
    {
        var suggestion = Assert.Single(AssetPathDisplayService.CreateSuggestions([fullPath]));
        Assert.Equal(fullPath, suggestion.FullValue);
        Assert.Equal(display, suggestion.DisplayValue);
    }

    [Fact]
    public void CreateSuggestions_UsesDistinctPrefixesForDuplicateFilenames()
    {
        var suggestions = AssetPathDisplayService.CreateSuggestions([
            @"resources\greek\buildings\armory\armory.xml",
            @"resources\norse\buildings\armory\armory.xml"]);
        Assert.Equal(@"greek\...\armory.xml", suggestions.Single(x => x.FullValue.Contains("greek")).DisplayValue);
        Assert.Equal(@"norse\...\armory.xml", suggestions.Single(x => x.FullValue.Contains("norse")).DisplayValue);
    }
}
