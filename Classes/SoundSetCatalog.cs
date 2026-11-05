using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>Metadata-only catalog of ProtoUnit sound-set XML files stored in Sound.bar.</summary>
public static class SoundSetCatalog
{
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ambient_sounds.xml",
        "playlist.xml",
        "soundmanifest.xml"
    };

    private static readonly ArchiveCatalogCache<IReadOnlyList<AnimFileCatalogEntry>> Cache = new(LoadCore);

    public static async Task<IReadOnlyList<AnimFileCatalogEntry>> LoadAsync(string archivePath)
        => (await LoadResultAsync(archivePath).ConfigureAwait(false)).Entries;

    public static async Task<CatalogLoadResult<AnimFileCatalogEntry>> LoadResultAsync(string archivePath)
    {
        try
        {
            return new(await Cache.LoadAsync(archivePath).ConfigureAwait(false),
                CatalogLoadState.Complete, archivePath, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return new([], CatalogLoadState.Unavailable, archivePath, [exception.Message]);
        }
    }

    public static async Task<string?> LoadXmlAsync(string archivePath, AnimFileCatalogEntry soundSet)
        => (await LoadXmlResultAsync(archivePath, soundSet).ConfigureAwait(false)).Value;

    public static Task<AssetPayloadResult<string>> LoadXmlResultAsync(string archivePath, AnimFileCatalogEntry soundSet)
    {
        ArgumentNullException.ThrowIfNull(soundSet);
        return AssetPayloadReader.LoadArchiveXmlAsync(archivePath, soundSet.Path);
    }

    internal static IReadOnlyList<AnimFileCatalogEntry> FilterArchiveEntries(IEnumerable<string> paths)
    {
        var results = new Dictionary<string, AnimFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            if (!TryGetGamePath(rawPath, out var gamePath) || ExcludedFiles.Contains(Path.GetFileName(gamePath)))
                continue;
            results.TryAdd(gamePath, new AnimFileCatalogEntry(gamePath, "Sound.bar"));
        }
        return results.Values.OrderBy(entry => Path.GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static bool TryGetGamePath(string rawPath, out string gamePath)
        => AnimFileCatalog.TryGetGamePath(rawPath, out gamePath);

    private static IReadOnlyList<AnimFileCatalogEntry> LoadCore(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        var archive = new BarArchive(stream);
        if (!archive.Load(out var error) || archive.Entries == null)
            throw new InvalidDataException($"Archive '{archivePath}' metadata could not be read: {error}.");
        return FilterArchiveEntries(archive.Entries.Select(entry => entry.RelativePath));
    }

}
