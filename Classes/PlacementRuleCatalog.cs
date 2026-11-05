using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>Placement-rule XML names relative to game/data/gameplay/placement_rules.</summary>
internal static class PlacementRuleCatalog
{
    private const string Folder = "game\\data\\gameplay\\placement_rules\\";

    internal static List<string> FromArchive(BarArchive? archive)
        => FilterArchivePaths(archive?.RootPath ?? "", archive?.Entries?.Select(entry => entry.RelativePath) ?? []);

    internal static List<string> FilterArchivePaths(string rootPath, IEnumerable<string> paths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths)
        {
            var path = raw.Replace('/', '\\');
            if (path.StartsWith('\\') || path.Contains(':') || path.Split('\\').Any(part => part is ".." or ".")) continue;
            if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
                path = rootPath.Replace('/', '\\').TrimEnd('\\') + "\\" + path;
            if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) continue;
            var name = path[Folder.Length..];
            if (name.EndsWith(".xml.xmb", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && name.Length > 4) names.Add(name);
        }
        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static List<string> WithCustomFiles(IEnumerable<string> baseNames, string? directory)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in baseNames) names[name] = name;
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            try
            {
                foreach (var path in ContainedFileEnumeration.EnumerateFiles(directory))
                {
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Path.GetRelativePath(directory, path).Replace('/', '\\');
                    names[name] = name;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine($"Placement-rule suggestions unavailable: {error.Message}"); }
        }
        return names.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
