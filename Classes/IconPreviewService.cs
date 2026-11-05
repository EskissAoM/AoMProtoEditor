using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

public sealed record IconPreviewData(byte[] PngBytes, string ResolvedSource);

internal sealed record IconPreviewCacheOptions(long MaximumBytes = 32L * 1024 * 1024,
    int MaximumEntries = 128, int MaximumConcurrentLoads = 2);
internal sealed record IconPreviewCacheState(int Entries, long Bytes, int Pending, int Active);

/// <summary>Resolves game-facing icon paths to previewable PNG data.</summary>
public sealed class IconPreviewService
{
    private readonly Func<string?> _archivePathProvider;
    private readonly Func<string?> _customResourcesDirectoryProvider;
    private readonly AssetPayloadPolicy _policy;
    private sealed record Revision(string Path, long Length, long WriteTicks)
    {
        internal string Key => $"{Path}|{Length}|{WriteTicks}";
        internal static Revision Capture(string path)
        {
            var info = new FileInfo(path);
            return new(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
        }
    }
    private sealed record CachedPreview(Revision Revision, IconPreviewData Data, long Access);
    private sealed record LoadRequest(string Id, string Source, Revision Revision, long Generation,
        Func<Task<AssetPayloadResult<IconPreviewData>>> Load);
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CachedPreview> _previewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<AssetPayloadResult<IconPreviewData>>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly IconPreviewCacheOptions _cacheOptions;
    private readonly SemaphoreSlim _loadSlots;
    private readonly Func<string, Task>? _beforeLoad;
    private string? _sourceContext;
    private long _generation, _access, _retainedBytes;
    private int _active;

    public IconPreviewService(
        Func<string?> archivePathProvider,
        Func<string?> customResourcesDirectoryProvider)
        : this(archivePathProvider, customResourcesDirectoryProvider, AssetPayloadPolicy.Default) { }

    internal IconPreviewService(Func<string?> archivePathProvider,
        Func<string?> customResourcesDirectoryProvider, AssetPayloadPolicy policy,
        IconPreviewCacheOptions? cacheOptions = null, Func<string, Task>? beforeLoad = null)
    {
        _policy = policy;
        _cacheOptions = cacheOptions ?? new IconPreviewCacheOptions();
        ArgumentOutOfRangeException.ThrowIfNegative(_cacheOptions.MaximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_cacheOptions.MaximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_cacheOptions.MaximumConcurrentLoads);
        _loadSlots = new SemaphoreSlim(_cacheOptions.MaximumConcurrentLoads);
        _beforeLoad = beforeLoad;
        _archivePathProvider = archivePathProvider ?? throw new ArgumentNullException(nameof(archivePathProvider));
        _customResourcesDirectoryProvider = customResourcesDirectoryProvider ??
                                            throw new ArgumentNullException(nameof(customResourcesDirectoryProvider));
    }

    public async Task<IconPreviewData?> LoadAsync(string? iconPath)
        => (await LoadResultAsync(iconPath).ConfigureAwait(false)).Value;

    public async Task<AssetPayloadResult<IconPreviewData>> LoadResultAsync(string? iconPath)
    {
        var normalizedPath = NormalizeGamePath(iconPath);
        try
        {
            var context = CaptureSourceContext();
            if (normalizedPath.Length == 0)
                return AssetPayloadResult<IconPreviewData>.Failure("", "No icon path.");
            var customSource = IconSourceResolver.ResolveCustom(context.Resources, normalizedPath);
            var customFile = customSource?.PreferredSource?.Path;
            if (customFile != null)
            {
                var revision = Revision.Capture(customFile);
                return await GetCachedAsync(new LoadRequest("file|" + revision.Path, revision.Path, revision,
                    context.Generation, () => LoadLooseFileAsync(revision.Path))).ConfigureAwait(false);
            }

            var archivePath = context.Archive;
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return AssetPayloadResult<IconPreviewData>.Failure(normalizedPath, "Icon or source archive was not found.");

            var archiveRevision = Revision.Capture(archivePath);
            lock (_cacheGate)
            {
                if (context.Generation == _generation && IsCurrent(archiveRevision))
                    RemoveSuperseded(archiveRevision);
            }
            var index = await IconCatalog.LoadEntryIndexResultAsync(archiveRevision.Path).ConfigureAwait(false);
            if (index.Value is not { } entries)
                return AssetPayloadResult<IconPreviewData>.Failure(index.Source, index.Error ?? "Icon archive catalog is unavailable.");

            var ddsPath = IconCatalog.EnumerateResourcePathAliases(ChangeExtension(normalizedPath, ".dds"))
                .FirstOrDefault(entries.ContainsKey);
            if (ddsPath == null)
                return AssetPayloadResult<IconPreviewData>.Failure(normalizedPath, "Icon was not found in the archive.");

            var entry = entries[ddsPath];
            return await GetCachedAsync(new LoadRequest($"bar|{archiveRevision.Path}|{ddsPath}",
                $"{archiveRevision.Path} :: {entry.RelativePath}", archiveRevision, context.Generation,
                () => LoadArchiveEntryAsync(archiveRevision.Path, entry))).ConfigureAwait(false);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        { return AssetPayloadResult<IconPreviewData>.Failure(normalizedPath, exception.Message); }
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

    // Called on mod refresh too, including when the new selection has no icon.
    internal void RefreshSourceContext() => CaptureSourceContext();

    internal IconPreviewCacheState GetCacheState()
    {
        lock (_cacheGate) return new(_previewCache.Count, _retainedBytes, _pending.Count, _active);
    }

    private (string? Archive, string? Resources, long Generation) CaptureSourceContext()
    {
        var archive = _archivePathProvider();
        var resources = _customResourcesDirectoryProvider();
        static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { return path; }
        }
        var key = Normalize(archive) + "\0" + Normalize(resources);
        lock (_cacheGate)
        {
            if (!string.Equals(key, _sourceContext, StringComparison.OrdinalIgnoreCase))
            {
                _sourceContext = key;
                _generation++;
                _previewCache.Clear();
                _retainedBytes = 0;
            }
            return (archive, resources, _generation);
        }
    }

    private Task<AssetPayloadResult<IconPreviewData>> GetCachedAsync(LoadRequest request)
    {
        var key = request.Id + "\0" + request.Revision.Key;
        TaskCompletionSource<AssetPayloadResult<IconPreviewData>> completion;
        lock (_cacheGate)
        {
            if (request.Generation == _generation)
            {
                if (!IsCurrent(request.Revision))
                    return Task.FromResult(AssetPayloadResult<IconPreviewData>.Failure(request.Source, "Icon source changed. Retry the preview."));
                // An archive revision replaces every cached entry from that archive.
                RemoveSuperseded(request.Revision);
                if (_previewCache.TryGetValue(request.Id, out var cached))
                {
                    _previewCache[request.Id] = cached with { Access = ++_access };
                    return Task.FromResult(AssetPayloadResult<IconPreviewData>.Success(cached.Data, request.Source));
                }
            }
            if (_pending.TryGetValue(key, out var pending)) return pending;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(key, completion.Task);
        }
        _ = ProduceAsync(request, key, completion);
        return completion.Task;
    }

    private static bool IsCurrent(Revision revision)
    {
        try { return Revision.Capture(revision.Path) == revision; }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception)) { return false; }
    }

    private async Task ProduceAsync(LoadRequest request, string key,
        TaskCompletionSource<AssetPayloadResult<IconPreviewData>> completion)
    {
        AssetPayloadResult<IconPreviewData>? result = null;
        Exception? failure = null;
        var entered = false;
        try
        {
            await _loadSlots.WaitAsync().ConfigureAwait(false);
            entered = true;
            lock (_cacheGate) _active++;
            if (!IsCurrent(request.Revision))
                result = AssetPayloadResult<IconPreviewData>.Failure(request.Source, "Icon source changed while queued. Retry the preview.");
            else
            {
                if (_beforeLoad != null) await _beforeLoad(request.Source).ConfigureAwait(false);
                result = await request.Load().ConfigureAwait(false);
                lock (_cacheGate)
                {
                    if (!IsCurrent(request.Revision))
                        result = AssetPayloadResult<IconPreviewData>.Failure(request.Source, "Icon source changed while loading. Retry the preview.");
                    else if (result.Value != null && request.Generation == _generation)
                        Store(request, result.Value);
                }
            }
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            lock (_cacheGate)
            {
                _pending.Remove(key);
                if (entered) _active--;
            }
            if (entered) _loadSlots.Release();
        }
        // Faults and unsuccessful results are never retained. Complete after cleanup
        // so a caller can retry immediately, without sharing the failed task again.
        if (failure != null) completion.TrySetException(failure);
        else completion.TrySetResult(result!);
    }

    private void Store(LoadRequest request, IconPreviewData data)
    {
        var bytes = data.PngBytes.LongLength;
        if (_cacheOptions.MaximumEntries == 0 || bytes > _cacheOptions.MaximumBytes) return;
        RemoveCached(request.Id);
        while (_previewCache.Count >= _cacheOptions.MaximumEntries || _retainedBytes > _cacheOptions.MaximumBytes - bytes)
            RemoveCached(_previewCache.MinBy(pair => pair.Value.Access).Key);
        _previewCache.Add(request.Id, new CachedPreview(request.Revision, data, ++_access));
        _retainedBytes += bytes;
    }

    private void RemoveCached(string id)
    {
        if (_previewCache.Remove(id, out var cached)) _retainedBytes -= cached.Data.PngBytes.LongLength;
    }

    private void RemoveSuperseded(Revision revision)
    {
        foreach (var old in _previewCache.Where(pair =>
                     pair.Value.Revision.Path.Equals(revision.Path, StringComparison.OrdinalIgnoreCase) &&
                     !pair.Value.Revision.Key.Equals(revision.Key, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
            RemoveCached(old);
    }

    private async Task<AssetPayloadResult<IconPreviewData>> LoadLooseFileAsync(string path)
    {
        try
        {
            var bytes = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return AssetPayloadReader.ReadStream(stream, _policy.MaximumImageBytes);
            }).ConfigureAwait(false);
            if (Path.GetExtension(path).Equals(".dds", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = await DdsIconDecoder.ConvertResultAsync(bytes, _policy).ConfigureAwait(false);
                if (decoded.Value == null) return AssetPayloadResult<IconPreviewData>.Failure(path, decoded.Error ?? "DDS preview could not be decoded.");
                bytes = decoded.Value;
            }
            else _policy.ValidatePng(bytes);
            return AssetPayloadResult<IconPreviewData>.Success(new IconPreviewData(bytes, path), path);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        {
            return AssetPayloadResult<IconPreviewData>.Failure(path, exception.Message);
        }
    }

    private async Task<AssetPayloadResult<IconPreviewData>> LoadArchiveEntryAsync(
        string archivePath,
        BarArchiveEntry entry)
    {
        try
        {
            var bytes = await Task.Run(() =>
            {
                using var stream = File.OpenRead(archivePath);
                return AssetPayloadReader.ReadEntry(stream, entry, _policy.MaximumImageBytes);
            }).ConfigureAwait(false);
            var decoded = await DdsIconDecoder.ConvertResultAsync(bytes, _policy).ConfigureAwait(false);
            var source = $"{archivePath} :: {entry.RelativePath}";
            return decoded.Value == null ? AssetPayloadResult<IconPreviewData>.Failure(source, decoded.Error ?? "DDS preview could not be decoded.") :
                AssetPayloadResult<IconPreviewData>.Success(new IconPreviewData(decoded.Value, source), source);
        }
        catch (Exception exception) when (AssetPayloadReader.IsReadFailure(exception))
        {
            return AssetPayloadResult<IconPreviewData>.Failure($"{archivePath} :: {entry.RelativePath}", exception.Message);
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
