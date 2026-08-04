namespace CryBarEditor.Classes;

public sealed record PathSuggestion(string FullValue, string DisplayValue)
{
    public override string ToString() => DisplayValue;
}

public static class AssetPathDisplayService
{
    private static readonly string[] IgnoredDisplayPrefixes = ["resources"];

    public static IReadOnlyList<PathSuggestion> CreateSuggestions(IEnumerable<string> values)
    {
        var paths = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .GroupBy(Normalize, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in paths.GroupBy(x => Parts(x).LastOrDefault() ?? x, StringComparer.OrdinalIgnoreCase))
        {
            var groupPaths = group.ToList();
            foreach (var path in groupPaths)
            {
                if (groupPaths.Count == 1) { labels[path] = group.Key; continue; }
                var parts = Parts(path).Take(Math.Max(0, Parts(path).Length - 1)).ToArray();
                var count = 1;
                while (count < parts.Length && groupPaths.Any(other => other != path && Parts(other).Take(count).SequenceEqual(parts.Take(count), StringComparer.OrdinalIgnoreCase))) count++;
                labels[path] = string.Join("\\", parts.Take(count)) + "\\...\\" + group.Key;
            }
        }
        return paths.Select(x => new PathSuggestion(x, labels[x])).OrderBy(x => x.DisplayValue, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Normalize(string value) => value.Replace('/', '\\').Trim();
    private static string[] Parts(string value)
    {
        var parts = Normalize(value).Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && IgnoredDisplayPrefixes.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) parts.RemoveAt(0);
        return parts.ToArray();
    }
}
