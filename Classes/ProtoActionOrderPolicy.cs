namespace AoMDivineDataEditor.Classes;

/// <summary>
/// Moves one action (or an inseparable linked-action group) without rebuilding
/// any action model. Keeping the existing objects is important because each model
/// may contain XML payload that the editor does not understand.
/// </summary>
internal static class ProtoActionOrderPolicy
{
    public static bool MoveGroup<T>(
        IList<T> orderedItems,
        IEnumerable<T> sourceItems,
        IEnumerable<T> targetItems,
        bool insertAfter)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        ArgumentNullException.ThrowIfNull(sourceItems);
        ArgumentNullException.ThrowIfNull(targetItems);

        var sourceSet = sourceItems.ToHashSet(ReferenceEqualityComparer.Instance);
        var targetSet = targetItems.ToHashSet(ReferenceEqualityComparer.Instance);
        if (sourceSet.Count == 0 || targetSet.Count == 0 || sourceSet.Overlaps(targetSet))
            return false;

        var originalOrder = orderedItems.ToList();
        var sourceGroup = originalOrder.Where(sourceSet.Contains).ToList();
        var targetGroup = originalOrder.Where(targetSet.Contains).ToList();
        if (sourceGroup.Count != sourceSet.Count || targetGroup.Count != targetSet.Count)
            return false;

        foreach (var sourceItem in sourceGroup)
            orderedItems.Remove(sourceItem);

        var targetIndexes = targetGroup
            .Select(orderedItems.IndexOf)
            .Where(index => index >= 0)
            .ToList();
        if (targetIndexes.Count != targetGroup.Count)
        {
            RestoreOriginalOrder(orderedItems, originalOrder);
            return false;
        }

        var insertionIndex = insertAfter
            ? targetIndexes.Max() + 1
            : targetIndexes.Min();
        foreach (var sourceItem in sourceGroup)
            orderedItems.Insert(insertionIndex++, sourceItem);

        if (orderedItems.SequenceEqual(originalOrder, ReferenceEqualityComparer.Instance))
            return false;

        return true;
    }

    private static void RestoreOriginalOrder<T>(IList<T> orderedItems, IReadOnlyList<T> originalOrder)
    {
        orderedItems.Clear();
        foreach (var item in originalOrder)
            orderedItems.Add(item);
    }
}
