using System.Xml.Linq;
using Avalonia.Controls;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class TechnologyEditorView
{
    private sealed class TechnologyXmlDraft(XmlEditSession session)
    {
        internal XmlEditSession Session { get; } = session;
        internal XmlEditSession.Snapshot? Prepared { get; set; }
        internal List<(string OldName, string NewName, IReadOnlySet<string> Ids)> StringMigrations { get; } = [];
    }

    private readonly Dictionary<string, TechnologyXmlDraft> _technologyXmlDrafts = new(StringComparer.OrdinalIgnoreCase);
    private XmlEditController _technologyXmlController = null!;
    private TechnologyXmlDraft? CurrentTechnologyXmlDraft => IsModifiedTab && _currentOriginalName != null
        ? _technologyXmlDrafts.GetValueOrDefault(_currentOriginalName) : null;
    internal bool HasXmlEditingSession => _technologyXmlDrafts.Values.Any(draft => draft.Session.IsEditing);

    private void RestoreTechnologyNameInput(XElement technology, string oldName)
    {
        if (!ReferenceEquals(_current, technology)) return;
        var loading = _loadingUi;
        _loadingUi = true;
        try { _techNameBox.Text = oldName; }
        finally { _loadingUi = loading; }
    }

    private void InitializeTechnologyXmlEditing()
    {
        _technologyXmlController = new XmlEditController(_xmlPreview, _editXmlButton, _technologyForm, MarkDirty)
        { BeforeEditAsync = PrepareTechnologyXmlEditingAsync };
    }

    private async Task<bool> PrepareTechnologyXmlEditingAsync()
    {
        try
        {
            await AwaitActiveEditorBuildCompletionAsync();
            if (!CanEditModifiedDocument || _current == null) return false;
            if (!await CommitCurrentTechnologyAsync()) return false;
            await AwaitActiveEditorBuildCompletionAsync();
            UpdatePreview();
            return CanEditModifiedDocument && _current != null;
        }
        catch (Exception exception)
        {
            await ShowTechnologyNameErrorAsync("XML editing unavailable", exception.Message);
            return false;
        }
    }

    private void BindTechnologyXmlPreview(string? preview = null)
    {
        if (_current == null || _currentOriginalName == null)
        {
            _technologyXmlController.Bind(null, false);
            return;
        }
        preview ??= FormatTechnologyXmlPreview(IsCurrentAdditiveTechnology
            ? _modified[_currentOriginalName] : _current);
        XmlEditSession session;
        if (CanEditModifiedDocument)
        {
            if (!_technologyXmlDrafts.TryGetValue(_currentOriginalName, out var draft))
            {
                draft = new TechnologyXmlDraft(new XmlEditSession(preview,
                    CanEditModifiedDocument && !string.IsNullOrWhiteSpace(_modTechtreePath)));
                _technologyXmlDrafts.Add(_currentOriginalName, draft);
            }
            session = draft.Session;
            session.RefreshFromModel(preview);
        }
        else session = new XmlEditSession(preview, false);
        _technologyXmlController.Bind(session, formEnabled: true, allowEditing: CanEditModifiedDocument);
    }

    private static string FormatTechnologyXmlPreview(XElement technology)
        => XmlSyntaxEditorService.PreparePreviewText(EditorXmlOrderPolicy.HasProtectedContent(technology)
            ? technology.ToString(SaveOptions.DisableFormatting)
            : XmlSyntaxEditorService.FormatElementForPreview(technology));

    private void MoveTechnologyXmlDraft(string oldName, string newName)
    {
        if (_technologyXmlDrafts.Remove(oldName, out var draft)) _technologyXmlDrafts[newName] = draft;
    }

    private void ForgetTechnologyXmlDraft(string name)
    {
        if (_technologyXmlDrafts.Remove(name, out var draft)) draft.Session.Reset(draft.Session.Text);
    }

    internal void ReleaseCleanTechnologyXmlDraft(string name)
    {
        if (_technologyXmlDrafts.GetValueOrDefault(name)?.Session.IsDirty == false) ForgetTechnologyXmlDraft(name);
    }

    private XElement ParseTechnologyXml(string name, string text)
    {
        if (!CanEditTechnology(name)) throw new InvalidDataException($"Technology '{name}' is read-only in this checkpoint.");
        XElement candidate;
        try { candidate = XElement.Parse(text, LoadOptions.PreserveWhitespace); }
        catch (System.Xml.XmlException exception)
        { throw new InvalidDataException($"Technology '{name}' has invalid XML: {exception.Message}", exception); }
        if (!candidate.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Technology '{name}' XML must have one <tech> root element.");
        var newName = GetCaseInsensitiveAttribute(candidate, "name")?.Value.Trim() ?? "";
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, name))
            throw new InvalidDataException($"Technology names can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
        if (_technologyXmlDrafts.TryGetValue(name, out var draft))
            foreach (var migration in draft.StringMigrations)
            {
                var before = new XElement(candidate);
                before.SetAttributeValue("name", migration.OldName);
                TechnologyStringIdPolicy.RenameOwnedStringIds(before, candidate, migration.NewName, migration.Ids);
            }
        return candidate;
    }

    private void RecordTechnologyXmlStringMigration(string oldName, string newName, IReadOnlyList<OwnedStringRename> changes)
    {
        if (changes.Count > 0 && _technologyXmlDrafts.TryGetValue(oldName, out var draft))
            draft.StringMigrations.Add((oldName, newName, changes.Select(change => change.OldId).ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    private XDocument CreateTechnologyXmlSnapshot()
    {
        var document = new XDocument(_modDocument);
        // Global Save prepares drafts before Technology.SaveAsync prepares them
        // again. The second pass must still validate migrated references, even
        // though its raw buffer has already been staged under the new name.
        var renames = _pendingTechnologyRenames.Select(rename => (Old: rename.OldName, New: rename.NewName)).ToList();
        foreach (var (name, draft) in _technologyXmlDrafts.Where(entry => entry.Value.Session.IsDirty))
        {
            if (draft.Prepared?.Text == draft.Session.Text) continue;
            var existing = FindTechnologyXmlElement(document, name)
                ?? throw new InvalidDataException($"Technology '{name}' is no longer available. Discard or reload its draft.");
            var candidate = ParseTechnologyXml(name, draft.Session.Text);
            var newName = GetCaseInsensitiveAttribute(candidate, "name")!.Value.Trim();
            existing.ReplaceWith(candidate);
            if (!name.Equals(newName, StringComparison.Ordinal)) renames.Add((name, newName));
        }
        var source = new TechnologyReferenceSource("Technology XML draft", document,
            TechnologyReferenceDocumentKind.Techtree, _modTechtreePath, CanMigrate: true);
        foreach (var rename in renames) TechnologyReferenceIndex.RenameRecognizedReferences(source, rename.Old, rename.New);
        return document;
    }

    private static XElement? FindTechnologyXmlElement(XDocument document, string name)
        => document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetCaseInsensitiveAttribute(element, "name")?.Value.Trim(), name, StringComparison.OrdinalIgnoreCase));

    internal async Task<bool> PrepareTechnologyXmlForSaveAsync()
    {
        if (!_technologyXmlDrafts.Values.Any(draft => draft.Session.IsEditing)) return true;
        await _technologyNameCommitGate.WaitAsync();
        try
        {
            if (_modDocumentSafetyError != null) throw new InvalidDataException(_modDocumentSafetyError);
            var pending = _technologyXmlDrafts.Where(entry => entry.Value.Session.IsEditing)
                .Select(entry => (Name: entry.Key, Draft: entry.Value, Snapshot: entry.Value.Session.CaptureForSave()!)).ToList();
            // Validate all raw buffers on a copy before staging any record. This
            // also exposes pending references to ordinary document validation.
            var validationDocument = CreateTechnologyXmlSnapshot();
            var shape = ValidateTechnologySourceStructure(validationDocument);
            if (!shape.IsValid) throw new InvalidDataException(shape.ToDisplayMessage(_modTechtreePath ?? "Technology draft"));
            var catalogs = CreateSaveValidationCatalogs() with
            {
                TechnologyNames = _original.Keys.Concat(validationDocument.Root!.Elements()
                    .Where(element => element.Name.LocalName.Equals("tech", StringComparison.OrdinalIgnoreCase))
                    .Select(element => GetCaseInsensitiveAttribute(element, "name")?.Value ?? ""))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            var validation = ValidateTechnologySaveCandidate(validationDocument, catalogs);
            if (!validation.IsValid) { await ShowSaveValidationErrorsAsync(validation.Errors, validation.Issues); return false; }

            foreach (var item in pending)
            {
                var draft = item.Draft;
                if (draft.Prepared?.Text == item.Snapshot.Text) continue;
                if (!_modified.TryGetValue(item.Name, out var original)) throw new InvalidDataException($"Technology '{item.Name}' is no longer available.");
                if (!draft.Session.IsDirty && draft.Prepared == null) { draft.Prepared = item.Snapshot; continue; }
                var candidate = ParseTechnologyXml(item.Name, item.Snapshot.Text);
                var newName = GetCaseInsensitiveAttribute(candidate, "name")!.Value.Trim();
                var linkedName = GetLinkedMinorGodTechnologyName(original);
                static bool AgeUpgrade(XElement element) => element.Elements().Any(child =>
                    child.Name.LocalName.Equals("flag", StringComparison.OrdinalIgnoreCase) && child.Value.Trim().Equals("AgeUpgrade", StringComparison.OrdinalIgnoreCase));
                var removeLinkedGod = AgeUpgrade(original) && !AgeUpgrade(candidate) && _hasLinkedMinorGod?.Invoke(linkedName) == true;
                if (removeLinkedGod && !_pendingMinorGodRemovals.Contains(linkedName))
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner || _confirmLinkedMinorGodRemovalAsync == null)
                        throw new InvalidDataException("Removing AgeUpgrade requires confirmation for its linked Minor God. No changes were saved.");
                    if (!await _confirmLinkedMinorGodRemovalAsync(owner, linkedName))
                    { ReportSaveFeedback(EditorSaveOutcome.Cancelled); return false; }
                }
                // The normal name pipeline must see the old identity while it
                // prepares reference/owned-string migration to the requested name.
                candidate.SetAttributeValue("name", item.Name);
                original.ReplaceWith(candidate);
                _modified[item.Name] = candidate;
                var selected = ReferenceEquals(_current, original);
                if (selected) _current = candidate;
                if (!await CommitPendingTechnologyNameAsync(candidate, newName))
                {
                    candidate.ReplaceWith(original);
                    _modified[item.Name] = original;
                    if (ReferenceEquals(_current, candidate)) _current = original;
                    return false;
                }
                draft.Prepared = item.Snapshot;
                if (removeLinkedGod) _pendingMinorGodRemovals.Add(linkedName);
                else if (AgeUpgrade(candidate)) _pendingMinorGodRemovals.Remove(linkedName);
                _dirtyTechnologyNames.Add(newName);
            }
            RecalculateDirtyState();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportSaveFeedback(EditorSaveOutcome.Blocked);
            await ShowTechnologyNameErrorAsync("Invalid Technology XML", exception.Message);
            return false;
        }
        finally { _technologyNameCommitGate.Release(); }
    }

    private List<(string Name, TechnologyXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> CapturePreparedTechnologyXmlSaves()
        => _technologyXmlDrafts.Where(entry => entry.Value.Prepared != null)
            .Select(entry => (entry.Key, entry.Value, entry.Value.Prepared!)).ToList();

    private void AcceptTechnologyXmlSaves(List<(string Name, TechnologyXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> saves)
    {
        foreach (var saved in saves)
        {
            if (!_technologyXmlDrafts.TryGetValue(saved.Name, out var current) || !ReferenceEquals(current, saved.Draft)) continue;
            if (FindTechnologyXmlElement(_modDocument, saved.Name) is not { } technology) continue;
            var acceptance = saved.Draft.Session.AcceptSaved(saved.Snapshot, FormatTechnologyXmlPreview(technology));
            if (acceptance == XmlSaveAcceptance.Saved) saved.Draft.StringMigrations.Clear();
            saved.Draft.Prepared = null;
        }
    }

    private void ReloadTechnologyXmlDrafts(IEnumerable<string> names)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_technologyXmlDrafts.TryGetValue(name, out var draft)) continue;
            if (FindTechnologyXmlElement(_modDocument, name) is { } technology)
            {
                draft.Session.Reset(FormatTechnologyXmlPreview(technology));
                draft.Prepared = null;
                draft.StringMigrations.Clear();
            }
            else ForgetTechnologyXmlDraft(name);
        }
    }
}
