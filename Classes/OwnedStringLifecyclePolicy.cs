using System.Collections.Frozen;

namespace AoMDivineDataEditor.Classes;

public sealed record OwnedStringSaveInspection(
    ModStringReferenceInspection References, IReadOnlySet<string> ReservedIds);

public enum OwnedStringRetentionReason
{
    UnverifiedOwnership,
    ReferencedByResult,
    ReservedByPendingEdit,
    UnchangedIdentity,
    MissingSourceValue,
    NotInStringTable,
    RenameDestination
}

/// <summary>Immutable proposals; applying local reference changes and persistence is the caller's responsibility.</summary>
public sealed class OwnedStringLifecyclePlan
{
    internal OwnedStringLifecyclePlan(IEnumerable<OwnedStringRename> referenceRenames,
        IDictionary<string, string> updates, IEnumerable<string> removals,
        IDictionary<string, OwnedStringRetentionReason> retained)
    {
        ReferenceRenames = Array.AsReadOnly(referenceRenames.ToArray());
        StringUpdates = updates.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        StringRemovals = removals.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        RetainedStrings = retained.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<OwnedStringRename> ReferenceRenames { get; }
    public IReadOnlyDictionary<string, string> StringUpdates { get; }
    public IReadOnlySet<string> StringRemovals { get; }
    public IReadOnlyDictionary<string, OwnedStringRetentionReason> RetainedStrings { get; }
}

/// <summary>
/// Pure decisions for one resulting lifecycle operation. Schema-specific helpers must
/// establish ownership and supply canonical old-to-final ID mappings before this call.
/// This policy neither infers ownership from prefixes nor discovers references or writes files.
/// </summary>
public static class OwnedStringLifecyclePolicy
{
    /// <param name="renames">Local field mappings from existing ownership helpers; not global replacements.</param>
    /// <param name="removalCandidates">Only keys affected by this operation, not a sweep of the string table.</param>
    /// <param name="verifiedOwnedIds">Proven owned IDs from the original owners, before their XML is changed.</param>
    /// <param name="existingEntries">Current mod string definitions; raw duplicate validation still belongs to StringTableDocument.</param>
    /// <param name="sourceValues">Resolved or edited source values, keyed by original ID. These override existing text only in the copied destination.</param>
    /// <param name="remainingReferenceIds">All references in the proposed result, other owners, external files, and pending drafts.</param>
    /// <param name="reservedIds">Keys claimed by other pending edits; never overwrite or delete them.</param>
    public static OwnedStringLifecyclePlan Prepare(
        IEnumerable<OwnedStringRename> renames,
        IEnumerable<string> removalCandidates,
        IReadOnlySet<string> verifiedOwnedIds,
        IReadOnlyDictionary<string, string> existingEntries,
        IReadOnlyDictionary<string, string> sourceValues,
        IReadOnlySet<string> remainingReferenceIds,
        IReadOnlySet<string> reservedIds)
    {
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(removalCandidates);
        ArgumentNullException.ThrowIfNull(verifiedOwnedIds);
        ArgumentNullException.ThrowIfNull(existingEntries);
        ArgumentNullException.ThrowIfNull(sourceValues);
        ArgumentNullException.ThrowIfNull(remainingReferenceIds);
        ArgumentNullException.ThrowIfNull(reservedIds);

        var owned = NormalizeIds(verifiedOwnedIds);
        var referenced = NormalizeIds(remainingReferenceIds);
        var reserved = NormalizeIds(reservedIds);
        var removalsToConsider = NormalizeIds(removalCandidates);
        var requested = renames.Select(rename => new OwnedStringRename(
                NormalizeId(rename.Field), NormalizeId(rename.OldId), NormalizeId(rename.NewId)))
            .DistinctBy(rename => $"{rename.Field}\0{rename.OldId}\0{rename.NewId}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var retained = new Dictionary<string, OwnedStringRetentionReason>(StringComparer.OrdinalIgnoreCase);
        var ownedRenames = requested.Where(rename => owned.Contains(rename.OldId)).ToList();
        var affectedIds = ownedRenames.SelectMany(rename => new[] { rename.OldId, rename.NewId })
            .Concat(removalsToConsider.Where(owned.Contains)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = NormalizeValues(existingEntries, affectedIds);
        var values = NormalizeValues(sourceValues, affectedIds);
        foreach (var rename in requested.Where(rename => !owned.Contains(rename.OldId)))
            retained[rename.OldId] = OwnedStringRetentionReason.UnverifiedOwnership;

        // One source shared by several fields must stay linked. Distinct sources must
        // not silently merge, even when their current text happens to be identical.
        foreach (var group in ownedRenames.GroupBy(rename => rename.OldId, StringComparer.OrdinalIgnoreCase))
            if (group.Select(rename => rename.NewId).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                throw new InvalidOperationException($"String ID '{group.Key}' has conflicting rename destinations. No strings were changed.");
        foreach (var group in ownedRenames.GroupBy(rename => rename.NewId, StringComparer.OrdinalIgnoreCase))
            if (group.Select(rename => rename.OldId).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                throw new InvalidOperationException($"Multiple string IDs would become '{group.Key}'. Choose another name. No strings were changed.");

        var approvedRenames = new List<OwnedStringRename>();
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rename in ownedRenames)
        {
            if (rename.OldId.Equals(rename.NewId, StringComparison.OrdinalIgnoreCase))
            {
                protectedIds.Add(rename.OldId);
                retained[rename.OldId] = OwnedStringRetentionReason.UnchangedIdentity;
                continue;
            }
            if (!values.TryGetValue(rename.OldId, out var text) && !existing.TryGetValue(rename.OldId, out text))
            {
                // Keep the original local reference instead of inventing text or a new dangling ID.
                protectedIds.Add(rename.OldId);
                retained[rename.OldId] = OwnedStringRetentionReason.MissingSourceValue;
                continue;
            }
            if (existing.ContainsKey(rename.NewId) || reserved.Contains(rename.NewId))
                throw new InvalidOperationException(
                    $"String ID '{rename.NewId}' already exists or is claimed by another edit. Choose another name. No strings were changed.");

            updates[rename.NewId] = text;
            approvedRenames.Add(rename);
            removalsToConsider.Add(rename.OldId);
        }

        var removals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in removalsToConsider)
        {
            if (!owned.Contains(id)) retained[id] = OwnedStringRetentionReason.UnverifiedOwnership;
            else if (protectedIds.Contains(id)) continue;
            else if (updates.ContainsKey(id)) retained[id] = OwnedStringRetentionReason.RenameDestination;
            else if (referenced.Contains(id)) retained[id] = OwnedStringRetentionReason.ReferencedByResult;
            else if (reserved.Contains(id)) retained[id] = OwnedStringRetentionReason.ReservedByPendingEdit;
            else if (!existing.ContainsKey(id)) retained[id] = OwnedStringRetentionReason.NotInStringTable;
            else removals.Add(id);
        }
        return new OwnedStringLifecyclePlan(approvedRenames, updates, removals, retained);
    }

    private static HashSet<string> NormalizeIds(IEnumerable<string> ids)
        => ids.Select(NormalizeId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id.Trim();
    }

    private static Dictionary<string, string> NormalizeValues(
        IReadOnlyDictionary<string, string> entries, IReadOnlySet<string> affectedIds)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, value) in entries)
        {
            var normalizedId = NormalizeId(id);
            if (!affectedIds.Contains(normalizedId)) continue;
            if (value is null)
                throw new InvalidOperationException($"String ID '{id}' has no value. No strings were changed.");
            if (!result.TryAdd(normalizedId, value))
                throw new InvalidOperationException($"String ID '{id}' has ambiguous definitions. No strings were changed.");
        }
        return result;
    }
}
