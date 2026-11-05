using System.Xml.Linq;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Classes;

/// <summary>Editor adapter for the shared ownership policy and retained reference inspection.</summary>
internal static class OwnedStringSavePreparation
{
    internal sealed record Result(IReadOnlyDictionary<string, string> Updates,
        OwnedStringLifecyclePlan Plan, ModStringReferenceInspection Inspection)
    {
        internal bool WritesStrings => Updates.Count > 0 || Plan.StringRemovals.Count > 0;
    }

    internal static Dictionary<string, string> ReadEntries(string? path)
        => string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? new(StringComparer.OrdinalIgnoreCase)
            : StringTableDocument.Parse(PreservedTextEncoding.Decode(File.ReadAllBytes(path)).Text).ToDictionary();

    internal static Result Prepare(string ownerPath, string? stringsPath,
        IReadOnlyDictionary<string, XDocument> proposed, IReadOnlySet<string> originalOwned,
        IReadOnlySet<string> currentOwned, IReadOnlyDictionary<string, string> pendingUpdates,
        IEnumerable<string> requestedRemovals,
        Func<IEnumerable<string>, IReadOnlyDictionary<string, XDocument>, OwnedStringSaveInspection>? inspect)
    {
        var candidates = requestedRemovals.Concat(originalOwned).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var context = inspect?.Invoke(candidates.Concat(pendingUpdates.Keys), proposed) ?? new OwnedStringSaveInspection(
            ModDeletionSafety.InspectStringReferences(ResolveRoot(ownerPath), candidates.Concat(pendingUpdates.Keys), proposed,
                excludedPaths: string.IsNullOrWhiteSpace(stringsPath) ? [] : [stringsPath]), new HashSet<string>());
        var existing = ReadEntries(stringsPath);
        foreach (var id in pendingUpdates.Keys.Where(currentOwned.Contains))
            if ((!originalOwned.Contains(id) && existing.ContainsKey(id)) || context.ReservedIds.Contains(id))
                throw new InvalidOperationException($"String ID '{id}' already exists or is claimed by another edit. Choose another name. No files were written.");
        var plan = OwnedStringLifecyclePolicy.Prepare([], candidates,
            originalOwned.Union(currentOwned).ToHashSet(StringComparer.OrdinalIgnoreCase), existing,
            new Dictionary<string, string>(), context.References.ReferencedIds, context.ReservedIds);
        var updates = pendingUpdates.Where(update => !candidates.Contains(update.Key) ||
                context.References.ReferencedIds.Contains(update.Key))
            .ToDictionary(update => update.Key, update => update.Value, StringComparer.OrdinalIgnoreCase);
        return new(updates, plan, context.References);
    }

    // Helpers mutate candidate copies. A missing source must leave the original local reference intact.
    internal static void KeepOnlyApprovedRenames(XElement before, XElement candidate,
        IReadOnlyList<OwnedStringRename> requested, OwnedStringLifecyclePlan plan)
    {
        var approved = plan.ReferenceRenames.Select(rename => (rename.OldId, rename.NewId)).ToHashSet();
        foreach (var pair in before.DescendantsAndSelf().Zip(candidate.DescendantsAndSelf()))
            if (!pair.First.HasElements && !pair.Second.HasElements && requested.Any(rename =>
                    rename.OldId.Equals(pair.First.Value.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    rename.NewId.Equals(pair.Second.Value.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !approved.Contains((rename.OldId, rename.NewId))))
                XmlTriviaPreserver.SetText(pair.Second, pair.First.Value);
    }

    internal static void ApplyLocalFieldChanges(XElement before, XElement candidate)
    {
        foreach (var pair in before.DescendantsAndSelf().Zip(candidate.DescendantsAndSelf()))
            if (!pair.First.HasElements && !pair.Second.HasElements && pair.First.Value != pair.Second.Value)
                XmlTriviaPreserver.SetText(pair.First, pair.Second.Value);
    }

    private static string ResolveRoot(string ownerPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(ownerPath))!);
        for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Name.Equals("game", StringComparison.OrdinalIgnoreCase) && ancestor.Parent != null)
                return ancestor.Parent.FullName;
        return directory.FullName;
    }
}
