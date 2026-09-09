namespace AoMDivineDataEditor.Classes;

/// <summary>Reconciles externally changed string values with pending Technology drafts.</summary>
public static class TechnologyStringMergePolicy
{
    public static HashSet<string> GetExternallyChangedIds(
        IReadOnlyDictionary<string, string> loadedValues,
        IReadOnlyDictionary<string, string> externalValues)
    {
        ArgumentNullException.ThrowIfNull(loadedValues);
        ArgumentNullException.ThrowIfNull(externalValues);

        return loadedValues.Keys.Concat(externalValues.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id =>
            {
                var hadLoadedValue = loadedValues.TryGetValue(id, out var loadedValue);
                var hasExternalValue = externalValues.TryGetValue(id, out var externalValue);
                return hadLoadedValue != hasExternalValue ||
                       (hadLoadedValue && !string.Equals(loadedValue, externalValue, StringComparison.Ordinal));
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> UseExternalVersionsForChangedIds(
        IReadOnlyCollection<string> externallyChangedIds,
        IDictionary<string, string> pendingUpdates,
        ISet<string> pendingRemovals)
    {
        ArgumentNullException.ThrowIfNull(externallyChangedIds);
        ArgumentNullException.ThrowIfNull(pendingUpdates);
        ArgumentNullException.ThrowIfNull(pendingRemovals);

        var discardedLocalChanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in externallyChangedIds)
        {
            if (pendingUpdates.Remove(id))
                discardedLocalChanges.Add(id);
            if (pendingRemovals.Remove(id))
                discardedLocalChanges.Add(id);
        }
        return discardedLocalChanges;
    }
}
