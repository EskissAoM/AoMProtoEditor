namespace AoMDivineDataEditor.Classes;

internal enum IconFileFormat { Png, Dds, Other }

// Path is absolute for a loose file and the actual entry path for an archive source.
internal sealed record IconPhysicalSource(string Path, IconFileFormat Format, bool IsCustom, string? ArchivePath = null);

internal sealed record IconAssetSource(string LogicalPath, IReadOnlyList<IconPhysicalSource> PhysicalSources)
{
    public bool IsCustom => PhysicalSources.Any(source => source.IsCustom);
    public bool HasCompetingCustomFiles => PhysicalSources.Count(source => source.IsCustom) > 1;
    public IconPhysicalSource? PreferredSource => PhysicalSources.FirstOrDefault(source => source.IsCustom)
                                                  ?? PhysicalSources.FirstOrDefault();
}

/// <summary>Shared logical/physical identity; does not authorize or perform file mutations.</summary>
internal static class IconSourceResolver
{
    internal static string ChangeExtension(string path, string extension)
        => System.IO.Path.ChangeExtension(path, extension);

    internal static string LogicalPath(string path)
    {
        var normalized = IconCatalog.NormalizePath(path);
        if (normalized.Length == 0) return "";
        if (System.IO.Path.GetExtension(normalized).Equals(".dds", StringComparison.OrdinalIgnoreCase) ||
            System.IO.Path.GetExtension(normalized).Length == 0)
            normalized = ChangeExtension(normalized, ".png");
        return normalized.StartsWith("resources\\", StringComparison.OrdinalIgnoreCase)
            ? normalized : "resources\\" + normalized;
    }

    internal static IconFileFormat Format(string path)
        => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => IconFileFormat.Png,
            ".dds" => IconFileFormat.Dds,
            _ => IconFileFormat.Other
        };

    internal static IconAssetSource? ResolveCustom(string? resourcesDirectory, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(resourcesDirectory) || !Directory.Exists(resourcesDirectory)) return null;
        var normalized = IconCatalog.NormalizePath(requestedPath);
        if (normalized.Length == 0) return null;
        var relative = IconCatalog.ToResourceRelativePath(normalized).Replace('\\', System.IO.Path.DirectorySeparatorChar);
        string root;
        try { root = System.IO.Path.GetFullPath(resourcesDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return null; }
        var sources = new List<IconPhysicalSource>();
        // Preserve preview precedence: exact requested file, PNG fallback, DDS fallback.
        foreach (var candidate in new[] { relative, ChangeExtension(relative, ".png"), ChangeExtension(relative, ".dds") }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, candidate));
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    sources.Add(new IconPhysicalSource(path, Format(path), true));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        return sources.Count == 0 ? null : new IconAssetSource(LogicalPath(normalized), sources.AsReadOnly());
    }

    internal static IconAssetSource Merge(string logicalPath, IEnumerable<IconPhysicalSource> sources)
        => new(LogicalPath(logicalPath), sources
            .DistinctBy(source => (source.ArchivePath ?? "") + "\0" + source.Path, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(source => source.IsCustom)
            .ThenBy(source => source.Format == IconFileFormat.Png ? 0 : 1)
            .ToList().AsReadOnly());
}
