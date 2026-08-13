using System.Collections.Concurrent;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Metadata-only catalog of ProtoUnit icon assets stored in UITextureCache.bar.
/// DDS payloads are never read or decompressed.
/// </summary>
public static class IconCatalog
{
    private static readonly HashSet<string> ExcludedResourceFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "buttons", "clouds", "credits", "front_end_demo", "campaign", "maps", "latitude",
        "in_game", "postgame", "purple_smoke", "shader", "textsprites", "front_end",
        "glyphs", "spectator", "talking_heads"
    };

    private static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<string>>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<IReadOnlyList<string>> LoadAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var info = new FileInfo(archivePath);
        var cacheKey = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Cache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<IReadOnlyList<string>>>(
                () => Task.Run(() => LoadCore(info.FullName)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
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

    private static IReadOnlyList<string> LoadCore(string archivePath)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            var archive = new BarArchive(stream);
            if (!archive.Load(out _) || archive.Entries == null)
                return [];
            return FilterPaths(archive.Entries.Select(entry => entry.RelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
