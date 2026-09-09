namespace AoMDivineDataEditor.GameData;

/// <summary>Parses Retold string_table.txt entries into their string IDs and values.</summary>
public static class StringTableParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return StringTableDocument.Parse(content).ToDictionary();
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
