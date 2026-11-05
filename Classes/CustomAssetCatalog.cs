namespace AoMDivineDataEditor.Classes;

/// <summary>Discovers loose game-facing assets inside the current local mod.</summary>
public static class CustomAssetCatalog
{
    internal const int MaximumDiscoveryEntries = 200_000;
    internal const int MaximumAssets = 20_000;

    // List adapters are suggestions only; production callers inspect availability.
    public static IReadOnlyList<string> LoadIconPaths(string? resourcesDirectory)
        => LoadIconPathsResult(resourcesDirectory).Entries;
    public static CatalogLoadResult<string> LoadIconPathsResult(string? resourcesDirectory)
        => LoadIconSourcesResult(resourcesDirectory).Map(sources => sources.Select(source => source.LogicalPath).ToList());
    internal static IReadOnlyList<IconAssetSource> LoadIconSources(string? resourcesDirectory)
        => LoadIconSourcesResult(resourcesDirectory).Entries;
    internal static CatalogLoadResult<IconAssetSource> LoadIconSourcesResult(string? resourcesDirectory)
        => DiscoverPaths(resourcesDirectory, [".png", ".dds"]).Map(paths => paths
            .Select(path => new { LogicalPath = IconSourceResolver.LogicalPath(Path.GetRelativePath(resourcesDirectory!, path)),
                Source = new IconPhysicalSource(path, IconSourceResolver.Format(path), true) })
            .GroupBy(item => item.LogicalPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => IconSourceResolver.Merge(group.Key, group.Select(item => item.Source)))
            .OrderBy(item => item.LogicalPath, StringComparer.OrdinalIgnoreCase).ToList());

    public static IReadOnlyList<AnimFileCatalogEntry> LoadAnimFiles(string? artDirectory)
        => LoadAnimFilesResult(artDirectory).Entries;
    public static IReadOnlyList<AnimFileCatalogEntry> LoadSoundSets(string? soundDirectory)
        => LoadSoundSetsResult(soundDirectory).Entries;
    public static CatalogLoadResult<AnimFileCatalogEntry> LoadAnimFilesResult(string? artDirectory)
        => LoadXmlAssetsResult(artDirectory);
    public static CatalogLoadResult<AnimFileCatalogEntry> LoadSoundSetsResult(string? soundDirectory)
        => LoadXmlAssetsResult(soundDirectory);
    private static CatalogLoadResult<AnimFileCatalogEntry> LoadXmlAssetsResult(string? rootDirectory)
        => DiscoverPaths(rootDirectory, [".xml"]).Map(paths => paths
            .Select(path => Path.GetRelativePath(rootDirectory!, path).Replace('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new AnimFileCatalogEntry(path, "Custom", IsCustom: true)).ToList());

    internal static CatalogLoadResult<string> DiscoverPaths(string? rootDirectory, IReadOnlyCollection<string> extensions,
        int maximumEntries = MaximumDiscoveryEntries, int maximumAssets = MaximumAssets,
        Func<string, IEnumerable<string>>? enumerate = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        var source = rootDirectory ?? "No custom folder configured";
        if (string.IsNullOrWhiteSpace(rootDirectory)) return Result(CatalogLoadState.Missing);
        var started = false;
        try
        {
            var root = Path.GetFullPath(rootDirectory);
            source = root;
            RequireRoot(root);
            started = true;
            var scanned = 0;
            var candidates = enumerate?.Invoke(root) ?? ContainedFileEnumeration.EnumerateFiles(root,
                includeHiddenAndSystem: true, maximumEntries: maximumEntries,
                skippedLink: path => { if (diagnostics.Count < 8) diagnostics.Add($"Linked path was not scanned: '{path}'."); });
            foreach (var path in candidates)
            {
                if (++scanned > maximumEntries) throw new IOException($"Discovery exceeded the {maximumEntries:N0} entry limit.");
                var fullPath = Path.GetFullPath(path);
                var relative = Path.GetRelativePath(root, fullPath);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                    throw new IOException($"Asset discovery escaped '{root}'.");
                if (!extensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase)) continue;
                if (!paths.Contains(fullPath) && paths.Count >= maximumAssets)
                    throw new IOException($"Discovery exceeded the {maximumAssets:N0} asset limit.");
                paths.Add(fullPath);
            }
            RequireRoot(root);
            return Result(diagnostics.Count == 0 ? CatalogLoadState.Complete : CatalogLoadState.Partial);
        }
        catch (Exception exception) when (!started && exception is FileNotFoundException or DirectoryNotFoundException)
        {
            try { RequireAccessibleParent(source); }
            catch (Exception parentError) when (parentError is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(parentError.Message);
                return Result(CatalogLoadState.Unavailable);
            }
            return Result(CatalogLoadState.Missing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            diagnostics.Add(exception.Message);
            return Result(paths.Count == 0 ? CatalogLoadState.Unavailable : CatalogLoadState.Partial);
        }
        CatalogLoadResult<string> Result(CatalogLoadState state)
            => new(paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(), state,
                source, diagnostics.AsReadOnly());
    }
    private static void RequireRoot(string root)
    {
        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.Directory) == 0) throw new IOException($"'{root}' is not a directory.");
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"The linked asset root '{root}' cannot be scanned safely.");
    }

    private static void RequireAccessibleParent(string root)
    {
        for (var parent = Path.GetDirectoryName(root); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(parent); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException($"'{parent}' is a file, so the custom asset folder cannot be reached.");
            return;
        }
        throw new IOException($"No accessible parent directory was found for '{root}'.");
    }
}
