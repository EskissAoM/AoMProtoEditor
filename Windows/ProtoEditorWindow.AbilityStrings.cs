using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.GameData;

namespace AoMDivineDataEditor.Windows;

public partial class ProtoEditorWindow
{
    private OwnedStringSavePreparation.Result PrepareAbilityStringSave(
        PreparedModXmlDocument powerFile, IReadOnlyList<(XElement Before, XElement After)> edits,
        IEnumerable<XElement> deleted, IReadOnlyDictionary<string, string>? editedEntries,
        GameIdentifierReferenceMigrationPlan? migration = null)
    {
        var existing = OwnedStringSavePreparation.ReadEntries(GetCurrentModStringsPath());
        var values = editedEntries ?? existing;
        var available = values.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var originals = edits.Select(edit => edit.Before).Concat(deleted).ToArray();
        var owned = originals.SelectMany(AbilityStringPolicy.CollectOwnedStringIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes = new List<OwnedStringRename>();
        foreach (var (before, after) in edits)
            if (!GetAbilityElementName(before).Equals(GetAbilityElementName(after), StringComparison.OrdinalIgnoreCase))
                changes.AddRange(AbilityStringPolicy.RenameOwnedStringIds(before, after, GetAbilityElementName(after), available));
        var currentOwned = edits.SelectMany(edit => AbilityStringPolicy.CollectOwnedStringIds(edit.After)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in edits.SelectMany(edit => AbilityStringPolicy.Fields(edit.After)))
        {
            var id = field.Value.Trim();
            if (editedEntries != null && editedEntries.TryGetValue(id, out var text) &&
                (!existing.TryGetValue(id, out var previous) || !text.Equals(previous, StringComparison.Ordinal)))
                AddAbilityStringUpdate(updates, id, text);
        }
        var proposed = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase) { [powerFile.Path] = powerFile.Document };
        foreach (var path in migration?.Paths ?? [])
            if (migration!.GetDocument(path) is { } document) proposed[Path.GetFullPath(path)] = document;
        var context = PrepareAbilityStringInspection(owned.Concat(updates.Keys).Concat(changes.Select(change => change.NewId)),
            proposed, powerFile.Path, originals.Select(GetAbilityElementName).Concat(edits.Select(edit => GetAbilityElementName(edit.After))));
        foreach (var id in updates.Keys.Where(currentOwned.Contains))
            if ((!owned.Contains(id) && existing.ContainsKey(id)) || context.ReservedIds.Contains(id))
                throw new InvalidOperationException($"Ability string '{id}' already exists or is claimed by another edit. No files were written.");
        var plan = OwnedStringLifecyclePolicy.Prepare(changes, owned, owned, existing, values,
            context.References.ReferencedIds, context.ReservedIds);
        foreach (var update in plan.StringUpdates) AddAbilityStringUpdate(updates, update.Key, update.Value);
        return new(updates, plan, context.References);
    }

    private OwnedStringSaveInspection PrepareAbilityStringInspection(IEnumerable<string> candidates,
        IReadOnlyDictionary<string, XDocument> proposed, string ownerPath, IEnumerable<string> affectedNames,
        IReadOnlySet<string>? appliedAbilityNames = null)
    {
        var context = PrepareOwnedStringSaveInspection(candidates, proposed, ownerPath, appliedAbilityNames, includePowerDrafts: true);
        var affected = affectedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reserved = context.ReservedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        reserved.UnionWith(_abilityPowerCatalog.Where(entry => !affected.Contains(entry.Key))
            .SelectMany(entry => GetAbilityDefinitionStringIds(entry.Value)));
        return new(context.References, reserved);
    }

    private static void AddAbilityStringUpdate(Dictionary<string, string> updates, string id, string text)
    {
        if (updates.TryGetValue(id, out var previous) && !previous.Equals(text, StringComparison.Ordinal))
            throw new InvalidOperationException($"Ability fields sharing '{id}' have conflicting text. No files were written.");
        updates[id] = text;
    }

    private async Task PersistAbilityChangesAsync(PreparedModXmlDocument powerFile,
        OwnedStringSavePreparation.Result cleanup, FileIntegrityTransaction.FileState stringsState,
        GameIdentifierReferenceMigrationPlan? migration = null,
        IEnumerable<ModDeletionInspection>? deletionChecks = null)
    {
        var stringPath = GetCurrentModStringsPath();
        var paths = (migration?.Paths ?? []).Select(path => (string?)path).ToList();
        if (cleanup.WritesStrings) paths.Add(stringPath);
        var expected = CaptureExpectedEditorFileStates(paths, migration?.ExpectedStates);
        if (!string.IsNullOrWhiteSpace(stringPath)) expected[Path.GetFullPath(stringPath)] = stringsState;
        cleanup.Inspection.MergeExpectedStatesInto(expected);
        await ModDocumentPersistence.ExecuteAsync([powerFile], paths, expected, async () =>
        {
            migration?.ValidateDiscovery();
            cleanup.Inspection.ValidateReadDependencies();
            foreach (var inspection in deletionChecks ?? []) inspection.ValidateReadDependencies();
            SaveAbilityXmlDocument(powerFile.Document, powerFile.Path);
            migration?.Save(SaveXmlDocumentAtomic);
            if (cleanup.WritesStrings)
                await SaveResolvedStringValuesAsync(cleanup.Updates, cleanup.Plan.StringRemovals.ToArray(),
                    StringTableSection.Abilities, removalsAlreadyChecked: true, updateSession: false);
        });
        AdoptCommittedManagerStrings(cleanup.WritesStrings);
        if (migration != null) AdoptGameIdentifierReferenceMigration(migration);
    }

    private sealed record PreparedAbilityDraftSave(
        IReadOnlyList<PreparedModXmlDocument> Documents,
        IReadOnlyDictionary<string, Dictionary<string, AbilityEditorDraft>> Drafts,
        IReadOnlySet<string> OriginalOwned, IReadOnlySet<string> CurrentOwned,
        IReadOnlyDictionary<string, string> Updates, IReadOnlySet<string> AffectedNames,
        string Metadata, FileIntegrityTransaction.FileState MetadataState);

    // Build all dirty Units' General/Power/binding documents before the enclosing Unit transaction.
    private PreparedAbilityDraftSave? PreparePendingAbilityDraftSave(IReadOnlyDictionary<string, string> entries)
    {
        var draftsByUnit = _dirtyUnitNames.Where(_unitAbilityDrafts.ContainsKey).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => CloneAbilityDrafts(_unitAbilityDrafts[name]), StringComparer.OrdinalIgnoreCase);
        if (draftsByUnit.Count == 0) return null;
        var generalPath = GetCurrentModAbilitiesFilePath("abilities_mods.xml")!;
        var powersPath = GetCurrentModGameplayFilePath("powers_mods.xml")!;
        var general = ModDocumentPersistence.Prepare(generalPath, ModXmlDocumentKind.Abilities);
        var powers = ModDocumentPersistence.Prepare(powersPath, ModXmlDocumentKind.Powers);
        var documents = new List<PreparedModXmlDocument> { general };
        var originals = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allocated = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
        var bindings = new Dictionary<string, IReadOnlyList<AbilityBindingMetadataRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (unitName, drafts) in draftsByUnit)
        {
            var associations = new List<XElement>();
            foreach (var draft in drafts.Values.Where(draft => !string.IsNullOrWhiteSpace(draft.Name)))
            {
                var source = draft.GeneralSource ?? new XElement("ability", new XText(draft.Name));
                var children = draft.GeneralValues.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => new XElement(pair.Key, pair.Value))
                    .Concat(draft.GeneralFlags.Select(flag => new XElement(flag, "1")));
                var association = ReplaceKnownAbilityChildren(source, AbilityGeneralFlags.Concat(AbilityGeneralValueTags), children);
                association.Nodes().OfType<XText>().Where(text => !string.IsNullOrWhiteSpace(text.Value)).Remove();
                association.AddFirst(new XText(draft.Name));
                associations.Add(association);
                if (!draft.Modified || draft.SharedPowerReference) continue;
                foreach (var tag in AbilityStringPolicy.FieldTags)
                {
                    if (!draft.PowerDisplayTexts.TryGetValue(tag, out var text)) continue;
                    var id = draft.PowerValues.GetValueOrDefault(tag, "").Trim();
                    if (!draft.ModifiedPowerDisplayTextTags.Contains(tag) && id.Length > 0) continue;
                    if (string.IsNullOrWhiteSpace(text)) { draft.PowerValues.Remove(tag); continue; }
                    if (id.Length == 0)
                    {
                        id = BuildUniqueAbilityStringId(draft.Name, tag == "rolloverid", allocated);
                        draft.PowerValues[tag] = id;
                    }
                    AddAbilityStringUpdate(updates, id, text);
                    allocated[id] = text;
                }
                var replacement = BuildAbilityPowerElement(draft);
                var existing = powers.Document.Root!.Elements("power").FirstOrDefault(power =>
                    GetAbilityElementName(power).Equals(draft.Name, StringComparison.OrdinalIgnoreCase));
                if (replacements.TryGetValue(draft.Name, out var other))
                {
                    if (!XNode.DeepEquals(NormalizeXmlElementForComparison(other), NormalizeXmlElementForComparison(replacement)))
                        throw new InvalidOperationException($"Pending Units have conflicting edits to Ability '{draft.Name}'. No files were written.");
                    continue;
                }
                // An external addition is outside this draft's edit when placement was
                // absent both at opening and in the edited result. Preserve the full
                // field; an opening field explicitly cleared by the user stays removed.
                if (draft.PowerSource != null &&
                    !draft.PowerSource.Elements().Any(field => field.Name.LocalName.Equals("placement", StringComparison.OrdinalIgnoreCase)) &&
                    !replacement.Elements().Any(field => field.Name.LocalName.Equals("placement", StringComparison.OrdinalIgnoreCase)) &&
                    existing?.Elements().FirstOrDefault(field => field.Name.LocalName.Equals("placement", StringComparison.OrdinalIgnoreCase)) is { } externalPlacement)
                    replacement.Add(new XElement(externalPlacement));
                MergeUnknownPlacementAttributes(existing, replacement);
                if (existing != null)
                {
                    originals[draft.Name] = new XElement(existing);
                    XmlTriviaPreserver.Preserve(existing, replacement);
                    existing.ReplaceWith(replacement);
                }
                else PowerCatalog.AddNewEntry(powers.Document, replacement, PowerDocumentSection.Abilities);
                replacements[draft.Name] = replacement;
            }
            var wrapper = general.Document.Root!.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(unitName, StringComparison.OrdinalIgnoreCase));
            if (wrapper == null)
            {
                if (associations.Count > 0) general.Document.Root.Add(new XElement(unitName, associations));
            }
            else
            {
                ManagedXmlChildren.Replace(wrapper, "ability", associations, GetAbilityElementName);
                if (!wrapper.HasAttributes && !wrapper.Elements().Any() && !wrapper.Nodes().Any(node =>
                        node is XComment or XProcessingInstruction || node is XText text && !string.IsNullOrWhiteSpace(text.Value))) wrapper.Remove();
            }
            bindings[unitName] = BuildAbilityBindingMetadataRecords(drafts.Values);
        }
        if (replacements.Count > 0) documents.Add(powers);
        var metadataState = FileIntegrityTransaction.CaptureState(AbilityBindingMetadataStore.StoragePath);
        var metadata = AbilityBindingMetadataStore.PrepareUnitReplacements(_modFilePath!, bindings, AbilityBindingMetadataStore.StoragePath);
        if (metadataState != FileIntegrityTransaction.CaptureState(AbilityBindingMetadataStore.StoragePath))
            throw new IOException("Ability binding metadata changed during preparation. No files were written.");
        return new(documents, draftsByUnit,
            originals.Values.SelectMany(AbilityStringPolicy.CollectOwnedStringIds).ToHashSet(StringComparer.OrdinalIgnoreCase),
            replacements.Values.SelectMany(AbilityStringPolicy.CollectOwnedStringIds).ToHashSet(StringComparer.OrdinalIgnoreCase),
            updates, replacements.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), metadata, metadataState);
    }

    private void SavePreparedAbilityDrafts(PreparedAbilityDraftSave? prepared)
    {
        if (prepared == null) return;
        foreach (var file in prepared.Documents) SaveAbilityXmlDocument(file.Document, file.Path);
        AbilityBindingMetadataStore.SavePrepared(AbilityBindingMetadataStore.StoragePath, prepared.Metadata);
    }

    private void AdoptPreparedAbilityDrafts(PreparedAbilityDraftSave? prepared)
    {
        if (prepared == null) return;
        var powers = prepared.Documents.FirstOrDefault(file => file.Kind == ModXmlDocumentKind.Powers)?.Document;
        foreach (var drafts in prepared.Drafts.Values)
            foreach (var draft in drafts.Values)
            {
                if (draft.Modified && !draft.SharedPowerReference)
                {
                    var power = powers!.Root!.Elements("power").Single(element =>
                        GetAbilityElementName(element).Equals(draft.Name, StringComparison.OrdinalIgnoreCase));
                    _abilityPowerCatalog[draft.Name] = new XElement(power);
                    RegisterAvailableAbilityName(draft.Name);
                    draft.PowerSource = new XElement(power);
                }
                draft.Modified = false;
                draft.ModifiedPowerDisplayTextTags.Clear();
            }
        foreach (var (name, drafts) in prepared.Drafts) _unitAbilityDrafts[name] = CloneAbilityDrafts(drafts);
        if (_currentUnitName != null && prepared.Drafts.TryGetValue(_currentUnitName, out var current))
        {
            _abilityDrafts.Clear();
            foreach (var pair in CloneAbilityDrafts(current)) _abilityDrafts[pair.Key] = pair.Value;
        }
    }
}
