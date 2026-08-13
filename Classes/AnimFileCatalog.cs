using System.Collections.Concurrent;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record AnimFileCatalogEntry(string Path, string ArchiveName, bool IsCustom = false);

/// <summary>
/// Metadata-only catalog of animation XML files stored in the Art*.bar archives.
/// Archive payloads are never read or decompressed.
/// </summary>
public static class AnimFileCatalog
{
    private static readonly HashSet<string> ExcludedArchiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ArtExamplesForModders.bar",
        "ArtUI.bar"
    };

    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<AnimFileCatalogEntry>>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<IReadOnlyList<AnimFileCatalogEntry>> LoadAsync(string artDirectory)
    {
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory))
            return Task.FromResult<IReadOnlyList<AnimFileCatalogEntry>>([]);

        var archives = Directory.EnumerateFiles(artDirectory, "Art*.bar", SearchOption.TopDirectoryOnly)
            .Where(ShouldScanArchive)
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (archives.Count == 0)
            return Task.FromResult<IReadOnlyList<AnimFileCatalogEntry>>([]);

        var cacheKey = string.Join('|', archives.Select(info =>
            $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}"));
        return Cache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<IReadOnlyList<AnimFileCatalogEntry>>>(
                () => Task.Run(() => LoadCore(archives)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static Task<string?> LoadXmlAsync(string artDirectory, AnimFileCatalogEntry animFile)
    {
        ArgumentNullException.ThrowIfNull(animFile);
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory) ||
            string.IsNullOrWhiteSpace(animFile.ArchiveName) ||
            !Path.GetFileName(animFile.ArchiveName).Equals(animFile.ArchiveName, StringComparison.Ordinal) ||
            !ShouldScanArchive(animFile.ArchiveName))
        {
            return Task.FromResult<string?>(null);
        }

        var archivePath = Path.Combine(artDirectory, animFile.ArchiveName);
        return File.Exists(archivePath)
            ? Task.Run(() => LoadXmlCore(archivePath, animFile.Path))
            : Task.FromResult<string?>(null);
    }

    public static async Task<string?> LoadCustomXmlAsync(string? artDirectory, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory) ||
            string.IsNullOrWhiteSpace(gamePath) || !gamePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(artDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, gamePath.Replace('\\', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
                return null;
            return await File.ReadAllTextAsync(candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static bool ShouldScanArchive(string archivePath)
        => !ExcludedArchiveNames.Contains(System.IO.Path.GetFileName(archivePath));

    internal static IReadOnlyList<AnimFileCatalogEntry> FilterArchiveEntries(
        string archiveName,
        IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var results = new Dictionary<string, AnimFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            var normalized = rawPath?.Trim().Replace('/', '\\') ?? "";
            if (!TryGetGamePath(normalized, out var gamePath))
                continue;

            if (gamePath.Length > 0)
                results.TryAdd(gamePath, new AnimFileCatalogEntry(gamePath, archiveName));
        }

        return results.Values
            .OrderBy(entry => GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? LoadXmlCore(string archivePath, string gamePath)
    {
        try
        {
            var normalizedGamePath = gamePath.Trim().Replace('/', '\\');
            using var stream = File.OpenRead(archivePath);
            var archive = new BarArchive(stream);
            if (!archive.Load(out _) || archive.Entries == null)
                return null;

            var entry = archive.Entries.FirstOrDefault(candidate =>
                TryGetGamePath(candidate.RelativePath, out var candidateGamePath) &&
                candidateGamePath.Equals(normalizedGamePath, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;

            var bytes = entry.ReadDataDecompressed(stream);
            if (entry.RelativePath.EndsWith(".XMB", StringComparison.OrdinalIgnoreCase))
                return XmbReader.ToFormattedXml(bytes);

            using var memory = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(memory, detectEncodingFromByteOrderMarks: true);
            var xml = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(xml) ? null : xml;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    internal static bool TryGetGamePath(string rawPath, out string gamePath)
    {
        var normalized = rawPath?.Trim().Replace('/', '\\') ?? "";
        if (normalized.EndsWith(".xml.XMB", StringComparison.OrdinalIgnoreCase))
        {
            gamePath = normalized[..^".XMB".Length];
            return true;
        }

        if (normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            gamePath = normalized;
            return true;
        }

        gamePath = "";
        return false;
    }

    private static IReadOnlyList<AnimFileCatalogEntry> LoadCore(IEnumerable<FileInfo> archives)
    {
        var results = new Dictionary<string, AnimFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var archiveInfo in archives)
        {
            try
            {
                using var stream = File.OpenRead(archiveInfo.FullName);
                var archive = new BarArchive(stream);
                if (!archive.Load(out _) || archive.Entries == null)
                    continue;

                foreach (var entry in FilterArchiveEntries(
                             archiveInfo.Name,
                             archive.Entries.Select(entry => entry.RelativePath)))
                {
                    results.TryAdd(entry.Path, entry);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A missing/unreadable optional archive must not prevent the remaining catalogs from loading.
            }
        }

        return results.Values
            .OrderBy(entry => GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOf('\\');
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}
