using System.Xml.Linq;

namespace AoMDivineDataEditor.Classes;

public sealed record PowerSavePreparationResult(
    IReadOnlyList<KeyValuePair<string, string>> Renames,
    IReadOnlyDictionary<string, string> StringUpdates,
    IReadOnlySet<string> StringRemovals)
{
    public bool WritesStrings => StringUpdates.Count > 0 || StringRemovals.Count > 0;
}

/// <summary>
/// Applies the deterministic, UI-independent preparation needed before a Power
/// document enters its cross-file save transaction.
/// </summary>
public static class PowerSavePreparation
{
    public static PowerSavePreparationResult Prepare(
        XDocument document,
        IEnumerable<string> dirtyNames,
        IEnumerable<KeyValuePair<string, string>> pendingRenames,
        IReadOnlyDictionary<string, string> pendingStringUpdates,
        IEnumerable<string> pendingStringRemovals)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dirtyNames);
        ArgumentNullException.ThrowIfNull(pendingRenames);
        ArgumentNullException.ThrowIfNull(pendingStringUpdates);
        ArgumentNullException.ThrowIfNull(pendingStringRemovals);

        NormalizeDirtyEntries(document, dirtyNames);
        var renames = pendingRenames.ToList();

        // Validate every rename before mutating the in-memory document. One unsupported
        // future field must not leave earlier renames partially applied after save aborts.
        foreach (var rename in renames)
        {
            var unsupported = GameIdentifierReferencePolicy.FindUnrecognizedReferences(
                document, GameIdentifierKind.Ability, rename.Key);
            if (unsupported.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{rename.Key}' was not renamed because powers_mods.xml contains unsupported power reference field(s): " +
                    string.Join(", ", unsupported) + ". No files were written.");
            }
        }

        foreach (var rename in renames)
        {
            GameIdentifierReferencePolicy.RenameReferences(
                document, GameIdentifierKind.Ability, rename.Key, rename.Value);
        }

        var updates = new Dictionary<string, string>(pendingStringUpdates, StringComparer.OrdinalIgnoreCase);
        var referencedLeafValues = document.Descendants()
            .Where(element => !element.HasElements)
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removals = pendingStringRemovals
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => !referencedLeafValues.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new PowerSavePreparationResult(renames, updates, removals);
    }

    private static void NormalizeDirtyEntries(XDocument document, IEnumerable<string> dirtyNames)
    {
        foreach (var name in dirtyNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var editedEntry = PowerCatalog.Find(document, name);
            if (editedEntry == null)
                continue;

            if (editedEntry.Attribute("type")?.Value.Equals("Bronze", StringComparison.OrdinalIgnoreCase) == true)
            {
                foreach (var armor in editedEntry.Elements().Where(element =>
                             element.Name.LocalName.Equals("armor", StringComparison.OrdinalIgnoreCase)))
                {
                    PowerCatalog.NormalizeNestedElementIndentation(armor);
                }
            }
            foreach (var effect in editedEntry.Elements().Where(element =>
                         element.Name.LocalName.Equals("effect", StringComparison.OrdinalIgnoreCase) &&
                         element.Elements().Any()))
            {
                PowerCatalog.NormalizeNestedElementIndentation(effect);
            }
            PowerCatalog.CollapseExcessiveBlankLines(editedEntry);
        }
    }
}
