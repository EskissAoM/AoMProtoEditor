using AoMDivineDataEditor.GameData;
using System.Text;
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

    [Fact]
    public void StringTableParser_reads_escaped_quotes_and_backslashes()
    {
        const string content = "ID = \"STR_TEST\" ; Str = \"Say \\\"hello\\\" at C:\\\\Mods\"\r\n";

        var parsed = StringTableParser.Parse(content);

        Assert.Equal("Say \"hello\" at C:\\Mods", parsed["STR_TEST"]);
    }

    [Fact]
    public void StringTableDocument_changes_only_requested_entry_and_preserves_structure()
    {
        const string content =
            "// custom preamble\r\n" +
            "Language = \"French\"\r\n" +
            "UnknownDirective = keep-me\r\n" +
            "\r\n" +
            "  ID=\"STR_FIRST\";Str=\"old\" // retained suffix\r\n" +
            "// between entries\r\n" +
            "ID = \"STR_SECOND\"   ;   Str = \"untouched\"\r\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string> { ["str_first"] = "new \"quoted\" value" },
            Array.Empty<string>());

        Assert.Equal(
            content.Replace("Str=\"old\"", "Str=\"new \\\"quoted\\\" value\"", StringComparison.Ordinal),
            updated);
    }

    [Fact]
    public void StringTableDocument_preserves_retold_backslash_sequences_when_updating()
    {
        const string content = "ID = \"STR_TEST\" ; Str = \"first\\nsecond\"\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string> { ["STR_TEST"] = "changed\\nsecond" },
            Array.Empty<string>());

        Assert.Equal("ID = \"STR_TEST\" ; Str = \"changed\\nsecond\"\n", updated);
    }

    [Fact]
    public void StringTableDocument_removes_only_owned_entry_and_appends_without_reordering()
    {
        const string content =
            "Language = \"English\"\n\n" +
            "ID = \"STR_Z\" ; Str = \"z\"\n" +
            "// keep this comment\n" +
            "ID = \"STR_REMOVE\" ; Str = \"gone\"\n" +
            "ID = \"STR_A\" ; Str = \"a\"\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string> { ["STR_NEW"] = "line one\nline two" },
            new[] { "STR_REMOVE" });

        Assert.Equal(
            "Language = \"English\"\n\n" +
            "ID = \"STR_Z\" ; Str = \"z\"\n" +
            "// keep this comment\n" +
            "ID = \"STR_A\" ; Str = \"a\"\n" +
            "ID = \"STR_NEW\"   ;   Str = \"line one\nline two\"\n",
            updated);
    }

    [Fact]
    public void StringTableDocument_refuses_ambiguous_duplicate_id_edits()
    {
        const string content =
            "ID = \"STR_DUP\" ; Str = \"first\"\n" +
            "ID = \"str_dup\" ; Str = \"second\"\n";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StringTableDocument.Parse(content).Apply(
                new Dictionary<string, string> { ["STR_DUP"] = "changed" },
                Array.Empty<string>()));

        Assert.Contains("defined more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StringTableDocument_does_not_parse_entry_like_text_inside_multiline_value()
    {
        const string content =
            "ID = \"STR_MULTI\" ; Str = \"First line\n" +
            "ID = \\\"NOT_AN_ENTRY\\\" ; Str = \\\"still part of the value\n" +
            "last line\"\n";

        var parsed = StringTableParser.Parse(content);

        Assert.Single(parsed);
        Assert.Contains("NOT_AN_ENTRY", parsed["STR_MULTI"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreservedTextEncoding_round_trips_utf8_bom_state(bool withBom)
    {
        const string text = "Language = \"English\"\r\nID = \"STR_É\" ; Str = \"été\"\r\n";
        var encoding = new UTF8Encoding(withBom);
        var bytes = (withBom ? encoding.GetPreamble() : [])
            .Concat(encoding.GetBytes(text))
            .ToArray();

        var decoded = PreservedTextEncoding.Decode(bytes);
        var roundTripped = decoded.Encode(decoded.Text);

        Assert.Equal(bytes, roundTripped);
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
