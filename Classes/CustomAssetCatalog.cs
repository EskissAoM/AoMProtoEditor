namespace AoMDivineDataEditor.Classes;

/// <summary>Discovers loose game-facing assets inside the current local mod.</summary>
public static class CustomAssetCatalog
{
    public static IReadOnlyList<string> LoadIconPaths(string? resourcesDirectory)
    {
        if (string.IsNullOrWhiteSpace(resourcesDirectory) || !Directory.Exists(resourcesDirectory))
            return [];

        try
        {
            return Directory.EnumerateFiles(resourcesDirectory, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(resourcesDirectory, path).Replace('/', '\\'))
                .Select(path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? path[..^".dds".Length] + ".png"
                    : path)
                .Select(path => "resources\\" + path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    public static IReadOnlyList<AnimFileCatalogEntry> LoadAnimFiles(string? artDirectory)
    {
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory))
            return [];

        try
        {
            return Directory.EnumerateFiles(artDirectory, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(artDirectory, path).Replace('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new AnimFileCatalogEntry(path, "Custom", IsCustom: true))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
