using System.Collections.Frozen;

namespace AoMDivineDataEditor.Classes;

/// <summary>Retained evidence for a batched string-reference check, not permission to delete unowned keys.</summary>
public sealed class ModStringReferenceInspection
{
    private readonly FileDiscoverySnapshot? _discovery;
    private readonly Action? _validateDocuments;

    internal ModStringReferenceInspection(IEnumerable<string> referencedIds,
        IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> expectedStates,
        FileDiscoverySnapshot? discovery = null, Action? validateDocuments = null)
    {
        ReferencedIds = referencedIds.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        ExpectedStates = expectedStates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _discovery = discovery;
        _validateDocuments = validateDocuments;
    }

    public IReadOnlySet<string> ReferencedIds { get; }
    public IReadOnlyDictionary<string, FileIntegrityTransaction.FileState> ExpectedStates { get; }
    public IReadOnlyList<string> DiscoveredPaths => _discovery?.Paths ?? [];

    /// <summary>Call inside the owner transaction before its first write, including string or metadata writes.</summary>
    public void ValidateReadDependencies()
    {
        _validateDocuments?.Invoke();
        _discovery?.Validate();
        FileIntegrityTransaction.Execute([], ExpectedStates, () => { });
    }

    /// <summary>Combines observations without replacing older baselines or partially merging a conflict.</summary>
    public void MergeExpectedStatesInto(IDictionary<string, FileIntegrityTransaction.FileState> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var normalized = new Dictionary<string, FileIntegrityTransaction.FileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, state) in destination)
        {
            var fullPath = Path.GetFullPath(path);
            if (normalized.TryGetValue(fullPath, out var previous) && previous != state)
                throw Conflict(fullPath);
            normalized[fullPath] = state;
        }
        var additions = new List<KeyValuePair<string, FileIntegrityTransaction.FileState>>();
        foreach (var (path, state) in ExpectedStates)
        {
            if (normalized.TryGetValue(path, out var previous))
            {
                if (previous != state) throw Conflict(path);
            }
            else additions.Add(new(path, state));
        }
        foreach (var (path, state) in additions) destination.Add(path, state);
    }

    private static IOException Conflict(string path) => new(
        $"'{path}' has conflicting file observations during string cleanup checking. Retry the operation. No files were written.");
}
