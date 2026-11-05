using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Metadata-only catalog of ProtoUnit icon assets stored in UITextureCache.bar.
/// DDS payloads are never read or decompressed.
/// </summary>
public static class IconCatalog
{
    private sealed record IconArchiveSnapshot(
        IReadOnlyList<string> Paths,
        IReadOnlyDictionary<string, BarArchiveEntry> Entries);

    private static readonly HashSet<string> ExcludedResourceFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "buttons", "clouds", "credits", "front_end_demo", "campaign", "maps", "latitude",
        "in_game", "postgame", "purple_smoke", "shader", "textsprites", "front_end",
        "glyphs", "spectator", "talking_heads"
    };

    private static readonly ArchiveCatalogCache<IconArchiveSnapshot> Cache = new(LoadCore);

    public static async Task<IReadOnlyList<string>> LoadAsync(string archivePath)
        => (await LoadResultAsync(archivePath).ConfigureAwait(false)).Entries;

    public static async Task<CatalogLoadResult<string>> LoadResultAsync(string archivePath)
        => (await LoadSnapshotAsync(archivePath).ConfigureAwait(false)).Map<string>(
            snapshots => snapshots.Count == 0 ? [] : snapshots[0].Paths);

    internal static async Task<IReadOnlyDictionary<string, BarArchiveEntry>> LoadEntryIndexAsync(string archivePath)
        => (await LoadEntryIndexResultAsync(archivePath).ConfigureAwait(false)).Value
            ?? new Dictionary<string, BarArchiveEntry>(StringComparer.OrdinalIgnoreCase);

    internal static async Task<AssetPayloadResult<IReadOnlyDictionary<string, BarArchiveEntry>>> LoadEntryIndexResultAsync(string archivePath)
    {
        var result = await LoadSnapshotAsync(archivePath).ConfigureAwait(false);
        return result.Entries.Count == 0
            ? AssetPayloadResult<IReadOnlyDictionary<string, BarArchiveEntry>>.Failure(archivePath, result.Warning)
            : AssetPayloadResult<IReadOnlyDictionary<string, BarArchiveEntry>>.Success(result.Entries[0].Entries, archivePath);
    }

    internal static async Task<IReadOnlyList<IconAssetSource>> LoadSourcesAsync(string archivePath)
        => (await LoadSourcesResultAsync(archivePath).ConfigureAwait(false)).Entries;

    internal static async Task<CatalogLoadResult<IconAssetSource>> LoadSourcesResultAsync(string archivePath)
    {
        var result = await LoadSnapshotAsync(archivePath).ConfigureAwait(false);
        return result.Map<IconAssetSource>(snapshots => snapshots.Count == 0 ? [] : snapshots[0].Paths.Select(path =>
        {
            var snapshot = snapshots[0];
            var alias = EnumerateResourcePathAliases(IconSourceResolver.ChangeExtension(path, ".dds"))
                .First(snapshot.Entries.ContainsKey);
            var entry = snapshot.Entries[alias];
            return new IconAssetSource(path, new[]
            {
                new IconPhysicalSource(entry.RelativePath, IconFileFormat.Dds, false, Path.GetFullPath(archivePath))
            });
        }).ToList().AsReadOnly());
    }

    private static async Task<CatalogLoadResult<IconArchiveSnapshot>> LoadSnapshotAsync(string archivePath)
    {
        try
        {
            return new([await Cache.LoadAsync(archivePath).ConfigureAwait(false)],
                CatalogLoadState.Complete, archivePath, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return new([], CatalogLoadState.Unavailable, archivePath, [exception.Message]);
        }
    }

    internal static IReadOnlyList<string> FilterPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            var normalized = rawPath?.Trim().Replace('/', '\\') ?? "";
            if (!normalized.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].Equals("resources", StringComparison.OrdinalIgnoreCase) ||
                ExcludedResourceFolders.Contains(parts[1]))
            {
                continue;
            }

            var gamePath = string.Join('\\', parts);
            results.Add(gamePath[..^".dds".Length] + ".png");
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IconArchiveSnapshot LoadCore(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        var archive = new BarArchive(stream);
        if (!archive.Load(out var error) || archive.Entries == null)
            throw new InvalidDataException($"Archive '{archivePath}' metadata could not be read: {error}.");
        var ddsEntries = archive.Entries
            .Where(entry => entry.RelativePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var entryIndex = ddsEntries
            .GroupBy(entry => NormalizePath(entry.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return new IconArchiveSnapshot(FilterPaths(ddsEntries.Select(entry => entry.RelativePath)), entryIndex);
    }

    internal static string NormalizePath(string? path)
        => (path ?? "").Trim().Replace('/', '\\').TrimStart('\\');

    internal static string ToResourceRelativePath(string path)
    {
        var normalized = NormalizePath(path);
        const string resourcesPrefix = "resources\\";
        return normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[resourcesPrefix.Length..]
            : normalized;
    }

    internal static IEnumerable<string> EnumerateResourcePathAliases(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized.Length == 0)
            yield break;

        yield return normalized;
        const string resourcesPrefix = "resources\\";
        if (normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            yield return ToResourceRelativePath(normalized);
        else
            yield return resourcesPrefix + normalized;
    }
}
