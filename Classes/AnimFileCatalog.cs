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

    private static readonly ArchiveCatalogCache<IReadOnlyList<AnimFileCatalogEntry>> Cache = new(LoadCore);

    public static async Task<IReadOnlyList<AnimFileCatalogEntry>> LoadAsync(string artDirectory)
        => (await LoadResultAsync(artDirectory).ConfigureAwait(false)).Entries;

    public static async Task<CatalogLoadResult<AnimFileCatalogEntry>> LoadResultAsync(string artDirectory)
    {
        var results = new Dictionary<string, AnimFileCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        var inspected = 0;
        var completed = 0;
        try
        {
            // Re-enumerate membership on every call. Only individual successful
            // archive snapshots are retained, never a partial directory result.
            foreach (var path in Directory.EnumerateFiles(artDirectory, "Art*.bar", SearchOption.TopDirectoryOnly)
                         .Where(ShouldScanArchive).Order(StringComparer.OrdinalIgnoreCase))
            {
                inspected++;
                try
                {
                    foreach (var entry in await Cache.LoadAsync(path).ConfigureAwait(false))
                        results.TryAdd(entry.Path, entry);
                    completed++;
                }
                catch (Exception exception) when (IsCatalogReadFailure(exception))
                {
                    diagnostics.Add($"Archive '{path}': {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (IsCatalogReadFailure(exception))
        {
            diagnostics.Add($"Art directory '{artDirectory}': {exception.Message}");
        }
        var state = diagnostics.Count == 0 ? CatalogLoadState.Complete :
            completed > 0 ? CatalogLoadState.Partial : CatalogLoadState.Unavailable;
        if (diagnostics.Count > 0)
            diagnostics.Insert(0, $"{completed} of {inspected} discovered Art archives loaded successfully.");
        return new(results.Values.OrderBy(entry => GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(),
            state, artDirectory, diagnostics.AsReadOnly());
    }

    private static bool IsCatalogReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException;

    public static async Task<string?> LoadXmlAsync(string artDirectory, AnimFileCatalogEntry animFile)
        => (await LoadXmlResultAsync(artDirectory, animFile).ConfigureAwait(false)).Value;

    public static Task<AssetPayloadResult<string>> LoadXmlResultAsync(string artDirectory, AnimFileCatalogEntry animFile)
        => LoadXmlResultCoreAsync(artDirectory, animFile, AssetPayloadPolicy.Default);

    internal static async Task<string?> LoadXmlForValidationAsync(string artDirectory, AnimFileCatalogEntry animFile)
        => (await LoadXmlResultCoreAsync(artDirectory, animFile, AssetPayloadPolicy.AnimationValidation).ConfigureAwait(false)).Value;

    private static Task<AssetPayloadResult<string>> LoadXmlResultCoreAsync(string artDirectory, AnimFileCatalogEntry animFile, AssetPayloadPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(animFile);
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory) ||
            string.IsNullOrWhiteSpace(animFile.ArchiveName) ||
            !Path.GetFileName(animFile.ArchiveName).Equals(animFile.ArchiveName, StringComparison.Ordinal) ||
            !ShouldScanArchive(animFile.ArchiveName))
        {
            return Task.FromResult(AssetPayloadResult<string>.Failure(artDirectory, "Animation archive or path is unavailable."));
        }

        var archivePath = Path.Combine(artDirectory, animFile.ArchiveName);
        return AssetPayloadReader.LoadArchiveXmlAsync(archivePath, animFile.Path, policy);
    }

    public static async Task<string?> LoadCustomXmlAsync(string? artDirectory, string gamePath)
        => (await LoadCustomXmlResultAsync(artDirectory, gamePath).ConfigureAwait(false)).Value;

    public static Task<AssetPayloadResult<string>> LoadCustomXmlResultAsync(string? artDirectory, string gamePath)
        => LoadCustomXmlResultCoreAsync(artDirectory, gamePath, AssetPayloadPolicy.Default);

    internal static async Task<string?> LoadCustomXmlForValidationAsync(string? artDirectory, string gamePath)
        => (await LoadCustomXmlResultCoreAsync(artDirectory, gamePath, AssetPayloadPolicy.AnimationValidation).ConfigureAwait(false)).Value;

    private static async Task<AssetPayloadResult<string>> LoadCustomXmlResultCoreAsync(string? artDirectory, string gamePath, AssetPayloadPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(artDirectory) || !Directory.Exists(artDirectory) ||
            string.IsNullOrWhiteSpace(gamePath) || !gamePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return AssetPayloadResult<string>.Failure(gamePath, "Custom XML folder or path is unavailable.");
        }

        try
        {
            var root = Path.GetFullPath(artDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, gamePath.Replace('\\', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
                return AssetPayloadResult<string>.Failure(candidate, "Custom XML path is missing or outside the asset folder.");
            return await AssetPayloadReader.LoadLooseXmlAsync(candidate, policy).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return AssetPayloadResult<string>.Failure(gamePath, exception.Message);
        }
    }

    /// <summary>
    /// Resolves both archive-relative paths and the short file references used by
    /// a number of stock ProtoUnits. A short name is accepted only when it is
    /// unambiguous within the supplied catalog.
    /// </summary>
    internal static AnimFileCatalogEntry? ResolveReference(
        IEnumerable<AnimFileCatalogEntry> entries,
        string? reference)
    {
        var normalized = reference?.Trim().Replace('/', '\\') ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var catalog = entries as IReadOnlyList<AnimFileCatalogEntry> ?? entries.ToList();
        var exact = catalog.FirstOrDefault(entry =>
            entry.Path.Replace('/', '\\').Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact != null || normalized.Contains('\\'))
            return exact;

        var fileMatches = catalog
            .Where(entry => GetFileName(entry.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return fileMatches.Count == 1 ? fileMatches[0] : null;
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

    private static IReadOnlyList<AnimFileCatalogEntry> LoadCore(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        var archive = new BarArchive(stream);
        if (!archive.Load(out var error) || archive.Entries == null)
            throw new InvalidDataException($"Archive '{archivePath}' metadata could not be read: {error}.");
        return FilterArchiveEntries(Path.GetFileName(archivePath), archive.Entries.Select(entry => entry.RelativePath));
    }

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOf('\\');
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}
