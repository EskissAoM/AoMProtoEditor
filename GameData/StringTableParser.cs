using System.Text.RegularExpressions;

namespace AoMDivineDataEditor.GameData;

/// <summary>Parses Retold string_table.txt entries into their string IDs and values.</summary>
public static partial class StringTableParser
{
    [GeneratedRegex(@"^\s*ID\s*=\s*""([^""]+)""\s*;\s*Str\s*=\s*""(.*)$", RegexOptions.Multiline)]
    private static partial Regex EntryStartPattern();

    public static Dictionary<string, string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = EntryStartPattern().Matches(content);

        for (int index = 0; index < matches.Count; index++)
        {
            Match match = matches[index];
            string firstLine = match.Groups[2].Value;
            int quote = firstLine.IndexOf('"');
            string value;

            if (quote >= 0)
            {
                value = firstLine[..quote];
            }
            else
            {
                int continuationStart = match.Index + match.Length;
                int continuationEnd = index + 1 < matches.Count ? matches[index + 1].Index : content.Length;
                ReadOnlySpan<char> continuation = content.AsSpan(continuationStart, continuationEnd - continuationStart);
                int endingQuote = continuation.IndexOf('"');
                value = endingQuote >= 0
                    ? firstLine + content.Substring(continuationStart, endingQuote)
                    : firstLine + continuation.TrimEnd().ToString();
            }

            result[match.Groups[1].Value] = value;
        }

        return result;
    }

    public static string? FindValue(string content, string key)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return content.Contains(key, StringComparison.OrdinalIgnoreCase)
            ? Parse(content).GetValueOrDefault(key)
            : null;
    }
}
