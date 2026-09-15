using System.Text;
using System.Text.RegularExpressions;

namespace AoMDivineDataEditor.GameData;

public enum StringTableSection
{
    Units,
    Abilities,
    Technologies,
    MajorGods,
    Cultures,
    UnitCommands
}

/// <summary>
/// Parses and edits a Retold string table without reconstructing unrelated text.
/// Comments, headers, ordering, whitespace, line endings, and unknown lines are
/// retained byte-for-byte unless the corresponding entry is changed or removed.
/// </summary>
public sealed partial class StringTableDocument
{
    private readonly string _source;
    private readonly IReadOnlyList<Entry> _entries;

    private StringTableDocument(string source, IReadOnlyList<Entry> entries)
    {
        _source = source;
        _entries = entries;
    }

    [GeneratedRegex("(?m)^[\\t ]*ID[\\t ]*=[\\t ]*\"(?<id>(?:\\\\.|[^\"\\r\\n])*)\"[\\t ]*;[\\t ]*Str[\\t ]*=[\\t ]*\"")]
    private static partial Regex EntryStartPattern();

    public static StringTableDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var entries = new List<Entry>();
        var coveredUntil = 0;

        foreach (Match match in EntryStartPattern().Matches(content))
        {
            if (match.Index < coveredUntil)
                continue;

            var valueStart = match.Index + match.Length;
            var valueEnd = FindClosingQuote(content, valueStart);
            if (valueEnd < 0)
            {
                var id = DecodeQuotedValue(match.Groups["id"].Value);
                throw new FormatException($"String-table entry '{id}' has no closing quote.");
            }

            var lineEnd = FindLineEndIncludingNewline(content, valueEnd + 1);
            entries.Add(new Entry(
                DecodeQuotedValue(match.Groups["id"].Value),
                DecodeQuotedValue(content[valueStart..valueEnd]),
                match.Index,
                lineEnd,
                valueStart,
                valueEnd,
                BuildRemovalReplacement(content, match.Index, valueEnd, lineEnd)));
            coveredUntil = lineEnd;
        }

        return new StringTableDocument(content, entries);
    }

    public Dictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
            result[entry.Id] = entry.Value;
        return result;
    }

    public static string GetSectionMarker(StringTableSection section)
        => $"// === AoM: DDE ==  {GetSectionLabel(section)} ===";

    public string ApplySnapshot(
        IReadOnlyDictionary<string, string> desiredEntries,
        StringTableSection additionSection)
    {
        ArgumentNullException.ThrowIfNull(desiredEntries);

        var desiredEntriesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in desiredEntries)
        {
            if (!desiredEntriesById.TryAdd(entry.Key, entry.Value))
                throw new InvalidOperationException(
                    $"String-table snapshot contains ID '{entry.Key}' more than once with different casing.");
        }

        var currentEntries = ToDictionary();
        var updates = desiredEntriesById
            .Where(entry =>
                !currentEntries.TryGetValue(entry.Key, out var currentValue) ||
                !string.Equals(currentValue, entry.Value, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        var removals = currentEntries.Keys
            .Where(id => !desiredEntriesById.ContainsKey(id))
            .ToArray();

        return Apply(updates, removals, additionSection);
    }

    public string Apply(
        IReadOnlyDictionary<string, string> updates,
        IReadOnlyCollection<string> removals)
        => Apply(updates, removals, additionSection: null);

    public string Apply(
        IReadOnlyDictionary<string, string> updates,
        IReadOnlyCollection<string> removals,
        StringTableSection additionSection)
        => Apply(updates, removals, (StringTableSection?)additionSection);

    private string Apply(
        IReadOnlyDictionary<string, string> updates,
        IReadOnlyCollection<string> removals,
        StringTableSection? additionSection)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(removals);

        var updateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var update in updates)
            updateValues[update.Key] = update.Value;

        var requestedIds = updateValues.Keys
            .Concat(removals)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ambiguous = _entries
            .Where(entry => requestedIds.Contains(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (ambiguous != null)
            throw new InvalidOperationException(
                $"String-table ID '{ambiguous.Key}' is defined more than once. The entry was not changed because ownership is ambiguous.");

        var removalIds = removals
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edits = new List<TextEdit>();
        foreach (var entry in _entries)
        {
            if (removalIds.Contains(entry.Id) && !updateValues.ContainsKey(entry.Id))
            {
                edits.Add(new TextEdit(entry.Start, entry.End, entry.RemovalReplacement));
                continue;
            }

            if (updateValues.TryGetValue(entry.Id, out var value) &&
                !string.Equals(entry.Value, value ?? "", StringComparison.Ordinal))
                edits.Add(new TextEdit(entry.ValueStart, entry.ValueEnd, EncodeQuotedValue(value ?? "")));
        }

        var output = new StringBuilder(_source);
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
        {
            output.Remove(edit.Start, edit.End - edit.Start);
            output.Insert(edit.Start, edit.Replacement);
        }

        var existingIds = _entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = updateValues
            .Where(update => !existingIds.Contains(update.Key))
            .OrderBy(update => update.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (additions.Count == 0)
            return output.ToString();

        var newline = DetectNewline(_source);
        if (additionSection != null)
        {
            AppendAdditionsToSection(output, additions, additionSection.Value, newline);
            return output.ToString();
        }

        if (output.Length == 0)
            output.Append("Language = \"English\"").Append(newline).Append(newline);
        else if (!EndsWithNewline(output))
            output.Append(newline);

        foreach (var addition in additions)
        {
            output.Append("ID = \"")
                .Append(EncodeQuotedValue(addition.Key))
                .Append("\"   ;   Str = \"")
                .Append(EncodeQuotedValue(addition.Value ?? ""))
                .Append('"')
                .Append(newline);
        }

        return output.ToString();
    }

    private static void AppendAdditionsToSection(
        StringBuilder output,
        IReadOnlyList<KeyValuePair<string, string>> additions,
        StringTableSection section,
        string newline)
    {
        var marker = GetSectionMarker(section);
        var source = output.ToString();
        var markerLines = FindSectionMarkerLines(source).ToList();
        var matchingSections = markerLines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Text.Equals(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matchingSections.Count > 1)
            throw new InvalidOperationException(
                $"String-table section '{marker}' is defined more than once. No strings were added because placement is ambiguous.");

        var sectionIndex = matchingSections.Count == 0 ? -1 : matchingSections[0].index;

        if (sectionIndex < 0)
        {
            if (output.Length == 0)
                output.Append("Language = \"English\"").Append(newline).Append(newline);
            else
            {
                if (!EndsWithNewline(output))
                    output.Append(newline);
                if (!EndsWithBlankLine(output, newline))
                    output.Append(newline);
            }

            output.Append(marker).Append(newline);
            AppendEntries(output, additions, newline);
            return;
        }

        var insertionIndex = sectionIndex + 1 < markerLines.Count
            ? markerLines[sectionIndex + 1].Start
            : output.Length;
        var entryBlock = new StringBuilder();
        if (insertionIndex == output.Length && !EndsWithNewline(output))
            entryBlock.Append(newline);
        AppendEntries(entryBlock, additions, newline);
        output.Insert(insertionIndex, entryBlock);
    }

    private static IEnumerable<SectionMarkerLine> FindSectionMarkerLines(string content)
    {
        for (var lineStart = 0; lineStart < content.Length;)
        {
            var lineEnd = lineStart;
            while (lineEnd < content.Length && content[lineEnd] is not '\r' and not '\n')
                lineEnd++;

            var text = content[lineStart..lineEnd].Trim();
            if (text.StartsWith("// === AoM: DDE ==  ", StringComparison.Ordinal) &&
                text.EndsWith(" ===", StringComparison.Ordinal))
            {
                yield return new SectionMarkerLine(lineStart, text);
            }

            if (lineEnd < content.Length && content[lineEnd] == '\r')
                lineEnd++;
            if (lineEnd < content.Length && content[lineEnd] == '\n')
                lineEnd++;
            lineStart = lineEnd;
        }
    }

    private static void AppendEntries(
        StringBuilder output,
        IEnumerable<KeyValuePair<string, string>> additions,
        string newline)
    {
        foreach (var addition in additions)
        {
            output.Append("ID = \"")
                .Append(EncodeQuotedValue(addition.Key))
                .Append("\"   ;   Str = \"")
                .Append(EncodeQuotedValue(addition.Value ?? ""))
                .Append('"')
                .Append(newline);
        }
    }

    private static string GetSectionLabel(StringTableSection section) => section switch
    {
        StringTableSection.Units => "Units",
        StringTableSection.Abilities => "Abilities",
        StringTableSection.Technologies => "Technologies",
        StringTableSection.MajorGods => "Major Gods",
        StringTableSection.Cultures => "Cultures",
        StringTableSection.UnitCommands => "Unit Commands",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
    };

    private static int FindClosingQuote(string content, int start)
    {
        for (var index = start; index < content.Length; index++)
        {
            if (content[index] != '"')
                continue;

            var slashCount = 0;
            for (var previous = index - 1; previous >= start && content[previous] == '\\'; previous--)
                slashCount++;
            if (slashCount % 2 == 0)
                return index;
        }
        return -1;
    }

    private static int FindLineEndIncludingNewline(string content, int start)
    {
        var index = start;
        while (index < content.Length && content[index] is not '\r' and not '\n')
            index++;
        if (index < content.Length && content[index] == '\r')
            index++;
        if (index < content.Length && content[index] == '\n')
            index++;
        return index;
    }

    private static string BuildRemovalReplacement(string content, int lineStart, int valueEnd, int lineEnd)
    {
        var contentEnd = lineEnd;
        while (contentEnd > valueEnd + 1 && content[contentEnd - 1] is '\r' or '\n')
            contentEnd--;

        var commentStart = content.IndexOf("//", valueEnd + 1, contentEnd - valueEnd - 1, StringComparison.Ordinal);
        if (commentStart < 0)
            return "";

        var indentationEnd = lineStart;
        while (indentationEnd < contentEnd && content[indentationEnd] is ' ' or '\t')
            indentationEnd++;
        return content[lineStart..indentationEnd] + content[commentStart..lineEnd];
    }

    private static string DecodeQuotedValue(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length && value[index + 1] is '\\' or '"')
            {
                result.Append(value[++index]);
                continue;
            }
            result.Append(value[index]);
        }
        return result.ToString();
    }

    private static string EncodeQuotedValue(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\')
            {
                // Retold strings commonly use sequences such as \n. Keep those
                // intact; only protect a literal slash where it would otherwise
                // participate in quote/backslash escaping or escape the delimiter.
                if (index + 1 == value.Length || value[index + 1] is '\\' or '"')
                    result.Append("\\\\");
                else
                    result.Append('\\');
                continue;
            }
            if (character == '"')
            {
                result.Append("\\\"");
                continue;
            }
            result.Append(character);
        }
        return result.ToString();
    }

    private static string DetectNewline(string content)
    {
        var newline = content.IndexOf('\n');
        return newline > 0 && content[newline - 1] == '\r' ? "\r\n" : "\n";
    }

    private static bool EndsWithNewline(StringBuilder value)
        => value.Length > 0 && value[^1] is '\r' or '\n';

    private static bool EndsWithBlankLine(StringBuilder value, string newline)
        => value.ToString().EndsWith(newline + newline, StringComparison.Ordinal);

    private sealed record Entry(
        string Id,
        string Value,
        int Start,
        int End,
        int ValueStart,
        int ValueEnd,
        string RemovalReplacement);
    private sealed record TextEdit(int Start, int End, string Replacement);
    private sealed record SectionMarkerLine(int Start, string Text);
}
