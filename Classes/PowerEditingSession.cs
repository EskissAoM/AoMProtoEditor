using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

internal enum PowerTransientArea
{
    UseLimit,
    LosProtoUnit,
    RestrictPlacement,
    OverlapType,
    RangeDecal,
    Notifications,
    MinimapNotifications,
    ActiveVisibility,
    ActiveListVisibility,
    CameraRange,
    OtherAttributePicker,
    OtherSpecificAttributePicker,
    TempestVfx,
    ThrowExcludePull,
    ThrowExcludeThrow,
    WitherKillTypes,
    CageKillSimilar,
    CageVfx,
    CageSound,
    ModifyVfx,
    SwitchingVfxAttach,
    FoxCharmVfx,
    CastMessage,
    CastSelfMessage,
    AlertPlayer,
    XmlPreviewCollapsed
}

internal sealed class PowerTransientUiState
{
    private readonly HashSet<PowerTransientArea> _openAreas = [];

    public HashSet<string> OpenOtherAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> OpenOtherSpecificAttributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool this[PowerTransientArea area]
    {
        get => _openAreas.Contains(area);
        set
        {
            if (value) _openAreas.Add(area);
            else _openAreas.Remove(area);
        }
    }

    public void Reset()
    {
        _openAreas.Clear();
        OpenOtherAttributes.Clear();
        OpenOtherSpecificAttributes.Clear();
    }
}

internal sealed record PowerEditingCommitSnapshot(
    IReadOnlySet<string> DirtyNames,
    IReadOnlyList<KeyValuePair<string, string>> Renames,
    IReadOnlyDictionary<string, string> StringUpdates,
    IReadOnlySet<string> StringRemovals);

/// <summary>
/// Owns Power-editing state which must move atomically across selection, commit, discard,
/// and external-merge boundaries. Avalonia controls and XML mutation remain view-owned.
/// </summary>
internal sealed class PowerEditingSession
{
    private readonly Dictionary<string, string> _stringUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stringOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stringRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stringRemovalOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, string>> _renames = [];

    public XElement? CurrentEntry { get; private set; }
    public string? SelectedName { get; private set; }
    public string? LoadedEntryName { get; private set; }
    public bool RawEditorDirty { get; set; }
    public bool IsDirty { get; private set; }
    public bool HasPendingWork => _dirtyNames.Count > 0 || _stringUpdates.Count > 0 || _stringRemovals.Count > 0;
    public bool HasDirtyRecords => _dirtyNames.Count > 0;
    public PowerTransientUiState Transient { get; } = new();

    public void Select(string? name, XElement? entry)
    {
        SelectedName = name;
        LoadedEntryName = name;
        CurrentEntry = entry;
        RawEditorDirty = false;
    }

    public void AdoptEntry(string name, XElement entry)
    {
        SelectedName = name;
        LoadedEntryName = name;
        CurrentEntry = entry;
        RawEditorDirty = false;
    }

    public void ClearSelection()
        => Select(null, null);

    public void SetDirty(bool value) => IsDirty = value;

    public void MarkDirty(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) _dirtyNames.Add(name);
    }

    public void RenameDirty(string oldName, string newName)
    {
        _dirtyNames.Remove(oldName);
        _dirtyNames.Add(newName);
    }

    public bool IsPowerDirty(string name)
        => _dirtyNames.Contains(name) ||
           (CurrentEntry != null && RawEditorDirty &&
            PowerCatalog.GetName(CurrentEntry).Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool TryGetPendingString(string id, out string value)
        => _stringUpdates.TryGetValue(id, out value!);

    public void SetPendingString(string id, string value, string owner, bool preserveExisting = false)
    {
        CancelStringRemoval(id);
        if (preserveExisting) _stringUpdates.TryAdd(id, value);
        else _stringUpdates[id] = value;
        _stringOwners[id] = owner;
    }

    public void RemovePendingStringUpdate(string id)
    {
        _stringUpdates.Remove(id);
        _stringOwners.Remove(id);
    }

    public void ScheduleStringRemoval(string id, string owner)
    {
        RemovePendingStringUpdate(id);
        _stringRemovals.Add(id);
        _stringRemovalOwners[id] = owner;
    }

    public void CancelStringRemoval(string id)
    {
        _stringRemovals.Remove(id);
        _stringRemovalOwners.Remove(id);
    }

    public void ReassignPendingStringOwner(string oldOwner, string newOwner)
    {
        foreach (var id in _stringOwners.Where(pair =>
                     pair.Value.Equals(oldOwner, StringComparison.OrdinalIgnoreCase))
                 .Select(pair => pair.Key).ToList())
            _stringOwners[id] = newOwner;
    }

    public void AddOrCollapseRename(string oldName, string newName)
    {
        var chainedIndex = _renames.FindIndex(rename =>
            rename.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (chainedIndex >= 0)
        {
            var original = _renames[chainedIndex].Key;
            if (original.Equals(newName, StringComparison.OrdinalIgnoreCase))
                _renames.RemoveAt(chainedIndex);
            else
                _renames[chainedIndex] = new KeyValuePair<string, string>(original, newName);
            return;
        }
        _renames.Add(new KeyValuePair<string, string>(oldName, newName));
    }

    public string ReferenceNameForDeletion(string currentName)
    {
        var original = _renames.LastOrDefault(rename =>
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase)).Key;
        return string.IsNullOrWhiteSpace(original) ? currentName : original;
    }

    public void RemoveRenamesForDeletion(string referenceName, string currentName)
        => _renames.RemoveAll(rename =>
            rename.Key.Equals(referenceName, StringComparison.OrdinalIgnoreCase) ||
            rename.Value.Equals(currentName, StringComparison.OrdinalIgnoreCase));

    public void DiscardPowerState(string name, string restoredName)
    {
        var names = new[] { name, restoredName }.Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _renames.RemoveAll(rename => names.Contains(rename.Key) || names.Contains(rename.Value));
        foreach (var discarded in names) _dirtyNames.Remove(discarded);
        foreach (var id in _stringOwners.Where(pair => names.Contains(pair.Value))
                     .Select(pair => pair.Key).ToList())
            RemovePendingStringUpdate(id);
        foreach (var id in _stringRemovalOwners.Where(pair => names.Contains(pair.Value))
                     .Select(pair => pair.Key).ToList())
            CancelStringRemoval(id);
        RawEditorDirty = false;
    }

    public KeyValuePair<string, string> FindConflictingRename(IEnumerable<string> conflictingNames)
    {
        var conflicts = conflictingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _renames.FirstOrDefault(rename =>
            conflicts.Contains(rename.Key) || conflicts.Contains(rename.Value));
    }

    public void AcceptExternalReplacements(IEnumerable<string> replacedNames)
    {
        foreach (var name in replacedNames) _dirtyNames.Remove(name);
    }

    public PowerEditingCommitSnapshot CreateCommitSnapshot()
        => new(
            _dirtyNames.ToHashSet(StringComparer.OrdinalIgnoreCase),
            _renames.ToList(),
            new Dictionary<string, string>(_stringUpdates, StringComparer.OrdinalIgnoreCase),
            _stringRemovals.ToHashSet(StringComparer.OrdinalIgnoreCase));

    public void AcceptCommit()
    {
        _stringUpdates.Clear();
        _stringOwners.Clear();
        _stringRemovals.Clear();
        _stringRemovalOwners.Clear();
        _dirtyNames.Clear();
        _renames.Clear();
        RawEditorDirty = false;
    }
}
