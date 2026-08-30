using System.Collections.Concurrent;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record IconPreviewData(byte[] PngBytes, string ResolvedSource);

/// <summary>Resolves game-facing icon paths to previewable PNG data.</summary>
public sealed class IconPreviewService
{
    private readonly Func<string?> _archivePathProvider;
    private readonly Func<string?> _customResourcesDirectoryProvider;
    private readonly ConcurrentDictionary<string, Lazy<Task<IconPreviewData?>>> _previewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public IconPreviewService(
        Func<string?> archivePathProvider,
        Func<string?> customResourcesDirectoryProvider)
    {
        _archivePathProvider = archivePathProvider ?? throw new ArgumentNullException(nameof(archivePathProvider));
        _customResourcesDirectoryProvider = customResourcesDirectoryProvider ??
                                            throw new ArgumentNullException(nameof(customResourcesDirectoryProvider));
    }

    public async Task<IconPreviewData?> LoadAsync(string? iconPath)
    {
        var normalizedPath = NormalizeGamePath(iconPath);
        if (normalizedPath.Length == 0)
            return null;

        var customFile = ResolveCustomFile(normalizedPath);
        if (customFile != null)
        {
            var info = new FileInfo(customFile);
            var cacheKey = $"file|{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            return await GetCachedAsync(cacheKey, () => LoadLooseFileAsync(info.FullName)).ConfigureAwait(false);
        }

        var archivePath = _archivePathProvider();
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return null;

        var archiveInfo = new FileInfo(archivePath);
        var archiveKey = $"{archiveInfo.FullName}|{archiveInfo.Length}|{archiveInfo.LastWriteTimeUtc.Ticks}";
        var entries = await IconCatalog.LoadEntryIndexAsync(archiveInfo.FullName).ConfigureAwait(false);

        var ddsPath = ChangeExtension(normalizedPath, ".dds");
        if (!entries.TryGetValue(ddsPath, out var entry))
            return null;

        var entryKey = $"bar|{archiveKey}|{ddsPath}";
        return await GetCachedAsync(entryKey, () => LoadArchiveEntryAsync(archiveInfo.FullName, entry)).ConfigureAwait(false);
    }

    internal static string SelectPreferredPath(IEnumerable<(string Path, bool IsDefault)> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var candidates = paths
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .Select(candidate => (Path: candidate.Path.Trim(), candidate.IsDefault))
            .ToList();
        return candidates.FirstOrDefault(candidate => candidate.IsDefault).Path
               ?? candidates.FirstOrDefault().Path
               ?? "";
    }

    private async Task<IconPreviewData?> GetCachedAsync(
        string key,
        Func<Task<IconPreviewData?>> loader)
    {
        var lazy = _previewCache.GetOrAdd(
            key,
            _ => new Lazy<Task<IconPreviewData?>>(loader, LazyThreadSafetyMode.ExecutionAndPublication));
        var result = await lazy.Value.ConfigureAwait(false);
        if (result == null)
            _previewCache.TryRemove(new KeyValuePair<string, Lazy<Task<IconPreviewData?>>>(key, lazy));
        return result;
    }

    private string? ResolveCustomFile(string normalizedPath)
    {
        var resourcesDirectory = _customResourcesDirectoryProvider();
        if (string.IsNullOrWhiteSpace(resourcesDirectory) || !Directory.Exists(resourcesDirectory))
            return null;

        var relativePath = normalizedPath.StartsWith("resources\\", StringComparison.OrdinalIgnoreCase)
            ? normalizedPath["resources\\".Length..]
            : normalizedPath;
        string resourcesRoot;
        try
        {
            resourcesRoot = Path.GetFullPath(resourcesDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
        foreach (var candidate in new[]
                 {
                     Path.Combine(resourcesDirectory, relativePath),
                     Path.Combine(resourcesDirectory, ChangeExtension(relativePath, ".png")),
                     Path.Combine(resourcesDirectory, ChangeExtension(relativePath, ".dds"))
                 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullCandidate = Path.GetFullPath(candidate);
                if (fullCandidate.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullCandidate))
                    return fullCandidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed or rooted game-facing paths.
            }
        }
        return null;
    }

    private static async Task<IconPreviewData?> LoadLooseFileAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                bytes = await DdsIconDecoder.ConvertToPngBytesAsync(bytes).ConfigureAwait(false) ?? [];
            return bytes.Length == 0 ? null : new IconPreviewData(bytes, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<IconPreviewData?> LoadArchiveEntryAsync(
        string archivePath,
        BarArchiveEntry entry)
    {
        try
        {
            var bytes = await Task.Run(() =>
            {
                using var stream = File.OpenRead(archivePath);
                return entry.ReadDataDecompressed(stream);
            }).ConfigureAwait(false);
            var pngBytes = await DdsIconDecoder.ConvertToPngBytesAsync(bytes).ConfigureAwait(false);
            return pngBytes == null ? null : new IconPreviewData(pngBytes, $"{archivePath} :: {entry.RelativePath}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static string NormalizeGamePath(string? path)
        => IconCatalog.NormalizePath(path);

    private static string ChangeExtension(string path, string extension)
    {
        var currentExtension = Path.GetExtension(path);
        return currentExtension.Length == 0
            ? path + extension
            : path[..^currentExtension.Length] + extension;
    }
}
