using AoMDivineDataEditor.GameData;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class GameDataTextTests
{
    [Fact]
    public void StringTableParser_reads_single_and_multiline_values_case_insensitively()
    {
        const string content = """
            ID = "STR_SINGLE" ; Str = "Single value"
            ID = "STR_MULTI" ; Str = "First line
            second line"
            """;

        var parsed = StringTableParser.Parse(content);

        Assert.Equal("Single value", parsed["str_single"]);
        Assert.Equal("First line\nsecond line", parsed["STR_MULTI"].Replace("\r\n", "\n"));
        Assert.Equal("Single value", StringTableParser.FindValue(content, "str_single"));
        Assert.Null(StringTableParser.FindValue(content, "STR_MISSING"));
    }

    [Theory]
    [InlineData("units/villager_female.xml", "villager", true)]
    [InlineData("units/villager_female.xml", "VILLAGER*FEMALE", true)]
    [InlineData("units/villager_female.xml", "female*villager", false)]
    [InlineData("anything", "", true)]
    public void GlobMatcher_matches_picker_filters(string input, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.IsMatch(input, pattern));
    }
}
