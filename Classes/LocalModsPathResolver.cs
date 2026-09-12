namespace AoMDivineDataEditor.Classes;

public static class LocalModsPathResolver
{
    public static string? Resolve(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPath.Trim()));
        var directoryName = Path.GetFileName(path);
        var parentName = Path.GetFileName(Path.GetDirectoryName(path));

        if (string.Equals(directoryName, "local", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parentName, "mods", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return string.Equals(directoryName, "mods", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(path, "local")
            : Path.Combine(path, "mods", "local");
    }
}
