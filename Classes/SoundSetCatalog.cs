using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<AnimFileCatalogEntry>>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<IReadOnlyList<AnimFileCatalogEntry>> LoadAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return Task.FromResult<IReadOnlyList<AnimFileCatalogEntry>>([]);
        var info = new FileInfo(archivePath);
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Cache.GetOrAdd(key, _ => new Lazy<Task<IReadOnlyList<AnimFileCatalogEntry>>>(
            () => Task.Run(() => LoadCore(info.FullName)), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static Task<string?> LoadXmlAsync(string archivePath, AnimFileCatalogEntry soundSet)
    {
        ArgumentNullException.ThrowIfNull(soundSet);
        return !string.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath)
            ? Task.Run(() => LoadXmlCore(archivePath, soundSet.Path))
            : Task.FromResult<string?>(null);
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
        try
        {
            using var stream = File.OpenRead(archivePath);
            var archive = new BarArchive(stream);
            return archive.Load(out _) && archive.Entries != null
                ? FilterArchiveEntries(archive.Entries.Select(entry => entry.RelativePath))
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static string? LoadXmlCore(string archivePath, string gamePath)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            var archive = new BarArchive(stream);
            if (!archive.Load(out _) || archive.Entries == null) return null;
            var entry = archive.Entries.FirstOrDefault(candidate =>
                TryGetGamePath(candidate.RelativePath, out var candidatePath) &&
                candidatePath.Equals(gamePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;
            var bytes = entry.ReadDataDecompressed(stream);
            return entry.RelativePath.EndsWith(".XMB", StringComparison.OrdinalIgnoreCase)
                ? XmbReader.ToFormattedXml(bytes)
                : System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }
}
