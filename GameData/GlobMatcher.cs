using System.Text.RegularExpressions;

namespace AoMDivineDataEditor.GameData;

public static class GlobMatcher
{
    [ThreadStatic] private static string? _cachedPattern;
    [ThreadStatic] private static Regex? _cachedRegex;

    public static bool IsMatch(string input, string pattern)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrEmpty(pattern))
            return true;
        if (!pattern.Contains('*'))
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(_cachedPattern, pattern, StringComparison.Ordinal))
        {
            _cachedPattern = pattern;
            _cachedRegex = new Regex(
                Regex.Escape(pattern).Replace("\\*", ".*"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return _cachedRegex!.IsMatch(input);
    }
}
