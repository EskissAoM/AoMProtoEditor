namespace AoMDivineDataEditor.Classes;

/// <summary>Shares live loads; retains at most one successful revision per source.</summary>
internal sealed class ArchiveCatalogCache<T>(Func<string, T> load, int capacity = 32)
{
    private sealed record Revision(string Path, long Length, long WriteTicks)
    {
        internal string Key => $"{Path}|{Length}|{WriteTicks}";
        internal static Revision Capture(string path)
        {
            var file = new FileInfo(path);
            return new Revision(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
        }
    }
    private sealed record Snapshot(Revision Revision, T Value, long Access);
    private readonly object _gate = new();
    private readonly Dictionary<string, Snapshot> _completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Lazy<Task<T>>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private long _access;

    internal int RetainedCount { get { lock (_gate) return _completed.Count; } }
    internal int PendingCount { get { lock (_gate) return _pending.Count; } }

    internal Task<T> LoadAsync(string path)
    {
        var revision = Revision.Capture(path);
        Lazy<Task<T>> pending;
        lock (_gate)
        {
            if (_completed.TryGetValue(revision.Path, out var cached))
            {
                if (cached.Revision.Key.Equals(revision.Key, StringComparison.OrdinalIgnoreCase))
                {
                    _completed[revision.Path] = cached with { Access = ++_access };
                    return Task.FromResult(cached.Value);
                }
                _completed.Remove(revision.Path);
            }
            if (!_pending.TryGetValue(revision.Key, out pending!))
            {
                pending = new Lazy<Task<T>>(() => LoadCoreAsync(revision),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _pending.Add(revision.Key, pending);
            }
        }
        return pending.Value;
    }

    private async Task<T> LoadCoreAsync(Revision revision)
    {
        try
        {
            var result = await Task.Run(() => load(revision.Path)).ConfigureAwait(false);
            lock (_gate)
            {
                // Serialize the final revision check with publication. An older
                // caller must not replace a newer snapshot between these steps.
                if (Revision.Capture(revision.Path) != revision)
                    throw new IOException($"Archive '{revision.Path}' changed while its catalog was loading. Retry.");
                _completed[revision.Path] = new Snapshot(revision, result, ++_access);
                while (_completed.Count > Math.Max(1, capacity))
                    _completed.Remove(_completed.MinBy(pair => pair.Value.Access).Key);
            }
            return result;
        }
        finally
        {
            // A fault is never retained: retry must work at the same file identity.
            lock (_gate) _pending.Remove(revision.Key);
        }
    }
}
