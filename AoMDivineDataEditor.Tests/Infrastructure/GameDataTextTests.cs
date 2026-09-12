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

    [Fact]
    public void StringTableDocument_appends_missing_editor_section_after_existing_content()
    {
        const string content =
            "// user heading\r\n" +
            "Language = \"English\"\r\n\r\n" +
            "ID = \"STR_EXISTING\" ; Str = \"keep me here\"\r\n" +
            "// user footer\r\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string> { ["STR_NEW_UNIT"] = "New unit" },
            Array.Empty<string>(),
            StringTableSection.Units);

        Assert.Equal(
            content +
            "\r\n" +
            "// === AoM: DDE ==  Units ===\r\n" +
            "ID = \"STR_NEW_UNIT\"   ;   Str = \"New unit\"\r\n",
            updated);
    }

    [Fact]
    public void StringTableDocument_adds_to_existing_section_before_the_next_section()
    {
        const string content =
            "Language = \"English\"\n\n" +
            "// === AoM: DDE ==  Units ===\n" +
            "ID = \"STR_UNIT_OLD\" ; Str = \"Old unit\"\n" +
            "// === AoM: DDE ==  Technologies ===\n" +
            "ID = \"STR_TECH_OLD\" ; Str = \"Old technology\"\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string> { ["STR_UNIT_NEW"] = "New unit" },
            Array.Empty<string>(),
            StringTableSection.Units);

        Assert.Equal(
            "Language = \"English\"\n\n" +
            "// === AoM: DDE ==  Units ===\n" +
            "ID = \"STR_UNIT_OLD\" ; Str = \"Old unit\"\n" +
            "ID = \"STR_UNIT_NEW\"   ;   Str = \"New unit\"\n" +
            "// === AoM: DDE ==  Technologies ===\n" +
            "ID = \"STR_TECH_OLD\" ; Str = \"Old technology\"\n",
            updated);
    }

    [Fact]
    public void StringTableDocument_snapshot_keeps_existing_entries_in_place_and_sections_only_new_entries()
    {
        const string content =
            "// custom section\n" +
            "ID = \"STR_UNIT_EXISTING\" ; Str = \"Old\"\n" +
            "// keep between\n" +
            "ID = \"STR_UNRELATED\" ; Str = \"Unrelated\"\n";
        var desired = StringTableDocument.Parse(content).ToDictionary();
        desired["STR_UNIT_EXISTING"] = "Changed";
        desired["STR_UNIT_NEW"] = "New";

        var updated = StringTableDocument.Parse(content).ApplySnapshot(
            desired,
            StringTableSection.Units);

        Assert.Equal(
            "// custom section\n" +
            "ID = \"STR_UNIT_EXISTING\" ; Str = \"Changed\"\n" +
            "// keep between\n" +
            "ID = \"STR_UNRELATED\" ; Str = \"Unrelated\"\n" +
            "\n" +
            "// === AoM: DDE ==  Units ===\n" +
            "ID = \"STR_UNIT_NEW\"   ;   Str = \"New\"\n",
            updated);
    }

    [Fact]
    public void StringTableDocument_removal_preserves_an_inline_comment()
    {
        const string content =
            "  ID = \"STR_REMOVE\" ; Str = \"value\" // user's explanation\r\n" +
            "ID = \"STR_KEEP\" ; Str = \"keep\"\r\n";

        var updated = StringTableDocument.Parse(content).Apply(
            new Dictionary<string, string>(),
            new[] { "STR_REMOVE" });

        Assert.Equal(
            "  // user's explanation\r\n" +
            "ID = \"STR_KEEP\" ; Str = \"keep\"\r\n",
            updated);
    }

    [Fact]
    public void StringTableDocument_refuses_duplicate_editor_sections_for_additions()
    {
        const string content =
            "// === AoM: DDE ==  Units ===\n" +
            "ID = \"STR_ONE\" ; Str = \"one\"\n" +
            "// === AoM: DDE ==  units ===\n";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StringTableDocument.Parse(content).Apply(
                new Dictionary<string, string> { ["STR_TWO"] = "two" },
                Array.Empty<string>(),
                StringTableSection.Units));

        Assert.Contains("defined more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StringTableSection.Units, "// === AoM: DDE ==  Units ===")]
    [InlineData(StringTableSection.Abilities, "// === AoM: DDE ==  Abilities ===")]
    [InlineData(StringTableSection.Technologies, "// === AoM: DDE ==  Technologies ===")]
    [InlineData(StringTableSection.MajorGods, "// === AoM: DDE ==  Major Gods ===")]
    [InlineData(StringTableSection.UnitCommands, "// === AoM: DDE ==  Unit Commands ===")]
    public void StringTableDocument_uses_canonical_editor_section_markers(
        StringTableSection section,
        string expected)
    {
        Assert.Equal(expected, StringTableDocument.GetSectionMarker(section));
    }

    [Fact]
    public void StringTableDocument_snapshot_no_op_is_exactly_stable()
    {
        const string content =
            "// preserve every byte\r\n" +
            "ID=\"STR_PATH\";Str=\"C:\\\\Mods\\nLine\" // suffix\r\n";
        var document = StringTableDocument.Parse(content);

        var updated = document.ApplySnapshot(document.ToDictionary(), StringTableSection.Units);

        Assert.Equal(content, updated);
    }

    [Fact]
    public void StringTableDocument_section_edit_preserves_utf16_bom_and_crlf()
    {
        const string content =
            "Language = \"English\"\r\n\r\n" +
            "// user's text\r\n" +
            "ID = \"STR_EXISTING\" ; Str = \"existing\"\r\n";
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
        var decoded = PreservedTextEncoding.Decode(bytes);

        var updatedText = StringTableDocument.Parse(decoded.Text).Apply(
            new Dictionary<string, string> { ["STR_ABILITY_NEW"] = "New ability" },
            Array.Empty<string>(),
            StringTableSection.Abilities);
        var updatedBytes = decoded.Encode(updatedText);

        Assert.True(updatedBytes.AsSpan().StartsWith(encoding.GetPreamble()));
        Assert.Contains("// user's text\r\n", updatedText, StringComparison.Ordinal);
        Assert.Contains("// === AoM: DDE ==  Abilities ===\r\n", updatedText, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", updatedText.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
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
