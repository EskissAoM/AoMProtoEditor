namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Retains discovery membership separately from file-content fingerprints.
/// The supplied rediscovery policy keeps containment and scan limits with the caller.
/// </summary>
public sealed class FileDiscoverySnapshot
{
    private readonly HashSet<string> _paths;
    private readonly Func<IEnumerable<string>> _rediscover;

    private FileDiscoverySnapshot(IEnumerable<string> paths, Func<IEnumerable<string>> rediscover)
    {
        _paths = Normalize(paths);
        Paths = Array.AsReadOnly(_paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
        _rediscover = rediscover;
    }

    public IReadOnlyList<string> Paths { get; }

    /// <summary>
    /// Capture the complete discovered set before reading reference documents or
    /// filtering out owner documents handled elsewhere in the transaction.
    /// </summary>
    public static FileDiscoverySnapshot Capture(
        IEnumerable<string> discoveredPaths, Func<IEnumerable<string>> rediscover)
    {
        ArgumentNullException.ThrowIfNull(discoveredPaths);
        ArgumentNullException.ThrowIfNull(rediscover);
        return new FileDiscoverySnapshot(discoveredPaths, rediscover);
    }

    /// <summary>
    /// Call after preparation and again inside the save transaction before its first write.
    /// Content changes remain the responsibility of the transaction's expected file states.
    /// </summary>
    public void Validate()
    {
        // Enumerate fully even when a mismatch is already evident: discovery failures
        // must never be mistaken for a successfully inspected, empty reference set.
        var current = Normalize(_rediscover());
        if (!_paths.SetEquals(current))
            throw new IOException(
                "The mod reference file list changed during inspection. Retry the operation so references can be checked again. No files were written.");
    }

    private static HashSet<string> Normalize(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            normalized.Add(Path.GetFullPath(path));
        }
        return normalized;
    }
}
