using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class MajorGodEditorView
{
    private sealed class MajorGodXmlDraft(XmlEditSession session)
    {
        internal XmlEditSession Session { get; } = session;
        internal XmlEditSession.Snapshot? Prepared { get; set; }
        internal List<OwnedStringRename> StringMigrations { get; } = [];
    }

    private readonly Dictionary<string, MajorGodXmlDraft> _majorGodXmlDrafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _xmlPreparationGate = new(1, 1);
    private XmlEditController _majorGodXmlController = null!;
    private MajorGodXmlDraft? CurrentMajorGodXmlDraft => IsModifiedMode && _loadedEntryName != null
        ? _majorGodXmlDrafts.GetValueOrDefault(_loadedEntryName) : null;
    internal bool HasXmlEditingSession => _majorGodXmlDrafts.Values.Any(draft => draft.Session.IsEditing);
    internal event Action<string, string>? XmlMajorGodRenamed;

    private static string MajorGodXmlPreview(XElement entry)
        => XmlSyntaxEditorService.PreparePreviewText(FormatMajorGodPreview(entry));

    private void InitializeMajorGodXmlEditing()
    {
        _majorGodXmlController = new XmlEditController(_rawXmlEditor, _editXmlButton, _majorGodStructuredForm, MajorGodXmlChanged)
        { BeforeEditAsync = BeginMajorGodXmlEditingAsync };
    }

    private async Task<bool> BeginMajorGodXmlEditingAsync()
    {
        try
        {
            await _populationTask;
            if (!IsEditableMajorGod || _currentEntry == null || string.IsNullOrWhiteSpace(_modPath)) return false;
            if (!await CommitCurrentMajorGodAsync()) return false;
            await _populationTask;
            RefreshRawXmlFromCurrent();
            return IsEditableMajorGod && _currentEntry != null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { await ShowMajorGodXmlErrorAsync("XML editing unavailable", exception.Message); return false; }
    }

    private void BindMajorGodXmlPreview(string text)
    {
        text = XmlSyntaxEditorService.PreparePreviewText(text);
        if (_currentEntry == null || _loadedEntryName == null)
        {
            // Original shared data has no editable record identity, but remains copyable.
            _majorGodXmlController.Bind(new XmlEditSession(text, false), true);
            return;
        }
        XmlEditSession session;
        if (IsEditableMajorGod)
        {
            if (!_majorGodXmlDrafts.TryGetValue(_loadedEntryName, out var draft))
            {
                draft = new MajorGodXmlDraft(new XmlEditSession(text, !string.IsNullOrWhiteSpace(_modPath)));
                _majorGodXmlDrafts.Add(_loadedEntryName, draft);
            }
            session = draft.Session;
            session.RefreshFromModel(text);
        }
        else session = new XmlEditSession(text, false);
        _majorGodXmlController.Bind(session, true);
    }

    private void MajorGodXmlChanged()
    {
        _rawEditorDirty = CurrentMajorGodXmlDraft?.Session.IsDirty == true;
        MarkCurrentDirty();
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // Form and raw edits deliberately share the complete module validation rules.
    private string ValidateMajorGodCandidate(XElement candidate, string? oldName, XElement? existing, bool edited, bool validateContent = true)
    {
        ValidateCandidateStructure(candidate);
        if (edited) MajorGodCatalog.RemoveEmptyOptionalFields(candidate);
        var name = MajorGodCatalog.GetName(candidate);
        if (string.IsNullOrWhiteSpace(name)) throw ValidationSourceLocation.Attach(new InvalidDataException("Name cannot be empty."), candidate, "name");
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(name, oldName))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"Name can contain only {InternalNamePolicy.AllowedCharactersDescription}."), candidate, "name");
        if (MajorGodCatalog.Find(_modDocument!, name) is { } duplicate && !ReferenceEquals(duplicate, existing))
            throw ValidationSourceLocation.Attach(new InvalidDataException($"A modified major god named '{name}' already exists."), candidate, "name");
        if (FindMajorGodStringNamespaceCollision(name, oldName) is { } collision)
            throw new InvalidDataException($"Major God '{name}' would use the same generated string IDs as '{collision.Name}'. Choose another name.");
        // Navigation captures the rename, then the host offers Discard. Incomplete
        // optional fields must not trap the user before that decision. Save and
        // raw XML preparation still run all module validation below.
        if (!validateContent) return name;
        var key = ReadValue(candidate, "key");
        if (key.Length > 0 && !MajorGodCatalog.IsValidKey(key))
            throw ValidationSourceLocation.Attach(new InvalidDataException("Key must be exactly one capital letter from A to Z."), candidate, "key");
        var culture = ReadValue(candidate, "culture");
        if (culture.Length > 0 && _cultureNames.Count > 0 && !_cultureNames.Contains(culture, StringComparer.OrdinalIgnoreCase))
            throw ValidationSourceLocation.Attach(new InvalidDataException("Culture must be selected from the supported culture list."), candidate, "culture");
        ValidateSharedFields(candidate);
        ValidateAssociatedTechs(candidate);
        ValidateEconomicAttributes(candidate);
        ValidateSpecialMechanics(candidate);
        ValidateStartingUnits(candidate);
        ValidateStartingQueue(candidate);
        ValidateStartingResources(candidate);
        return name;
    }

    private XElement ParseMajorGodXml(string name, MajorGodXmlDraft draft, string text)
    {
        if (!CanEditMajorGod(name)) throw new InvalidDataException($"Major God '{name}' is read-only in this checkpoint.");
        var candidate = XElement.Parse(text, LoadOptions.PreserveWhitespace);
        if (candidate.Name != MajorGodCatalog.EntryName)
            throw new InvalidDataException("Raw XML must have one <civ> root element without a namespace.");
        MajorGodCatalog.ApplyOwnedResolvedStringRenames(candidate, draft.StringMigrations);
        var existing = MajorGodCatalog.Find(_modDocument!, name)
            ?? throw new InvalidDataException($"Major God '{name}' is no longer available. Discard or reload its draft.");
        ValidateMajorGodCandidate(candidate, name, existing, true);
        return candidate;
    }

    private static void ApplyMajorGodOwnerRenames(XDocument document, IEnumerable<KeyValuePair<string, string>> renames)
    {
        foreach (var rename in renames)
        {
            var unsupported = GameIdentifierReferencePolicy.FindUnrecognizedReferences(document, GameIdentifierKind.MajorGod, rename.Key);
            if (unsupported.Count > 0)
                throw new InvalidDataException($"Major God '{rename.Key}' has unsupported reference fields: {string.Join(", ", unsupported)}. No files were written.");
            GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.MajorGod, rename.Key, rename.Value);
        }
    }

    private XDocument? CreateMajorGodXmlSnapshot()
    {
        if (_modDocument == null) return null;
        var document = new XDocument(_modDocument);
        var renames = _pendingRenames.ToList();
        foreach (var (name, draft) in _majorGodXmlDrafts.Where(pair => pair.Value.Session.IsDirty))
        {
            if (draft.Prepared?.Text == draft.Session.Text) continue;
            var candidate = ParseMajorGodXml(name, draft, draft.Session.Text);
            MajorGodCatalog.Find(document, name)!.ReplaceWith(candidate);
            var newName = MajorGodCatalog.GetName(candidate);
            if (!name.Equals(newName, StringComparison.OrdinalIgnoreCase)) renames.Add(new(name, newName));
        }
        var safety = ModXmlDocumentSafety.Validate(document, ModXmlDocumentKind.MajorGods, ReadOnlyMajorGodNames(document));
        if (!safety.IsSafeToEdit) throw new InvalidDataException(string.Join(Environment.NewLine, safety.Errors.Select(error => error.Reason)));
        ApplyMajorGodOwnerRenames(document, renames);
        RequireUnchangedMajorGodPatches(document);
        return document;
    }

    internal async Task<bool> PrepareMajorGodXmlForSaveAsync()
    {
        await _xmlPreparationGate.WaitAsync();
        try
        {
            if (!HasXmlEditingSession) return true;
            _ = CreateMajorGodXmlSnapshot(); // Validate every retained draft before staging any.
            foreach (var (name, draft) in _majorGodXmlDrafts.Where(pair => pair.Value.Session.IsEditing).ToArray())
            {
                var snapshot = draft.Session.CaptureForSave()!;
                if (draft.Prepared?.Text == snapshot.Text) continue;
                if (!draft.Session.IsDirty) { draft.Prepared = snapshot; continue; }
                if (!await CommitMajorGodXmlCandidateAsync(name, draft, snapshot)) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportSaveFeedback(EditorSaveOutcome.Blocked);
            await ShowMajorGodXmlErrorAsync("Invalid Major God XML", exception.Message);
            return false;
        }
        finally { _xmlPreparationGate.Release(); }
    }

    private async Task<bool> CommitMajorGodXmlCandidateAsync(string oldName, MajorGodXmlDraft draft, XmlEditSession.Snapshot snapshot)
    {
        var entry = MajorGodCatalog.Find(_modDocument!, oldName)!;
        var candidate = ParseMajorGodXml(oldName, draft, snapshot.Text);
        var newName = MajorGodCatalog.GetName(candidate);
        MajorGodCatalog.EnsureStatsId(candidate);
        var beforeRename = new XElement(candidate);
        MajorGodCatalog.FindDirectChild(beforeRename, "name")!.Value = oldName;
        var changes = MajorGodCatalog.RenameOwnedResolvedStringIds(candidate, oldName, newName);
        var plan = await PrepareStringRenameAsync(beforeRename, candidate, changes,
            MajorGodCatalog.CollectOwnedStringIds(beforeRename), newName, reuseSavedOwner: true, sourceMajorGodName: oldName);
        // Discard/reload may complete during lookup. Newer text is retained, but a
        // retired epoch or replaced entry must never be resurrected by this save.
        if (!ReferenceEquals(_majorGodXmlDrafts.GetValueOrDefault(oldName), draft) ||
            draft.Session.CaptureForSave()?.Epoch != snapshot.Epoch ||
            !ReferenceEquals(MajorGodCatalog.Find(_modDocument!, oldName), entry)) return false;
        var selected = ReferenceEquals(entry, _currentEntry);
        ScheduleOwnedStringRemovals(MajorGodCatalog.CollectOwnedStringIds(entry).Except(MajorGodCatalog.CollectOwnedStringIds(candidate)), oldName);
        PruneUnreferencedPendingStringUpdates(oldName, candidate);
        ApplyMajorGodStringRenamePlan(candidate, oldName, newName, plan, selected);
        entry.ReplaceWith(candidate);
        draft.StringMigrations.AddRange(plan.ReferenceRenames);
        draft.Prepared = snapshot;
        MoveMajorGodXmlDraft(oldName, newName);
        _dirtyNames.Add(newName);
        if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        { AddOrCollapsePendingRename(oldName, newName); XmlMajorGodRenamed?.Invoke(oldName, newName); }
        if (selected)
        {
            _currentEntry = candidate; _loadedEntryName = CurrentMajorGodName = newName;
            _rawEditorDirty = false;
            SetRawXml(FormatMajorGodPreview(candidate));
            _populationTask = PopulateStructuredFieldsAsync(candidate);
        }
        SetDirty(true);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task ShowMajorGodXmlErrorAsync(string title, string message)
    {
        _statusMessage.Text = message;
        if ((TopLevel.GetTopLevel(this) as Window ?? _dialogOwner) is { } owner)
            await new Prompt(PromptType.Error, title, message).ShowDialog(owner);
    }

    private void MoveMajorGodXmlDraft(string oldName, string newName)
    { if (_majorGodXmlDrafts.Remove(oldName, out var draft)) _majorGodXmlDrafts[newName] = draft; }

    private void ForgetMajorGodXmlDraft(string name)
    { if (_majorGodXmlDrafts.Remove(name, out var draft)) draft.Session.Reset(draft.Session.Text); }

    internal void ReleaseCleanMajorGodXmlDraft(string? name)
    { if (name != null && _majorGodXmlDrafts.GetValueOrDefault(name)?.Session.IsDirty == false) ForgetMajorGodXmlDraft(name); }

    private void ReloadMajorGodXmlDrafts(IEnumerable<string> names)
    {
        foreach (var name in names.ToArray())
        {
            if (!_majorGodXmlDrafts.TryGetValue(name, out var draft)) continue;
            if (MajorGodCatalog.Find(_modDocument!, name) is { } entry)
            { draft.Session.Reset(MajorGodXmlPreview(entry)); draft.Prepared = null; draft.StringMigrations.Clear(); }
            else ForgetMajorGodXmlDraft(name);
        }
    }

    private void ReloadChangedCleanMajorGodXmlDrafts()
        => ReloadMajorGodXmlDrafts(_majorGodXmlDrafts.Where(pair => !pair.Value.Session.IsDirty &&
            (MajorGodCatalog.Find(_modDocument!, pair.Key) is not { } entry || pair.Value.Session.Text != MajorGodXmlPreview(entry)))
            .Select(pair => pair.Key).ToArray());

    internal void ReleaseMajorGodXmlEditing()
    {
        // Retire queued population/field callbacks before detaching the controller.
        ++_populateGeneration;
        ++_economicEditorGeneration;
        _isPopulating = true;
        _currentEntry = null;
        _loadedEntryName = CurrentMajorGodName = null;
        _majorGodXmlController.Dispose();
        foreach (var name in _majorGodXmlDrafts.Keys.ToArray()) ForgetMajorGodXmlDraft(name);
    }

    private List<(string Name, MajorGodXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> CapturePreparedMajorGodXmlSaves()
        => _majorGodXmlDrafts.Where(pair => pair.Value.Prepared != null)
            .Select(pair => (pair.Key, pair.Value, pair.Value.Prepared!)).ToList();

    private void AcceptMajorGodXmlSaves(IReadOnlyList<(string Name, MajorGodXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> saves)
    {
        foreach (var (name, draft, snapshot) in saves)
        {
            if (!ReferenceEquals(_majorGodXmlDrafts.GetValueOrDefault(name), draft) ||
                MajorGodCatalog.Find(_modDocument!, name) is not { } entry) continue;
            if (draft.Session.AcceptSaved(snapshot, MajorGodXmlPreview(entry)) == XmlSaveAcceptance.Saved) draft.StringMigrations.Clear();
            draft.Prepared = null;
            if (draft.Session.IsDirty) _dirtyNames.Add(name);
        }
        _rawEditorDirty = CurrentMajorGodXmlDraft?.Session.IsDirty == true;
        SetDirty(_dirtyNames.Count > 0 || _pendingStringUpdates.Count > 0 || _pendingStringRemovals.Count > 0 ||
                 _majorGodXmlDrafts.Values.Any(draft => draft.Session.IsDirty));
        if (_currentEntry != null) SetRawXml(FormatMajorGodPreview(MajorGodPreviewEntry(_currentEntry)));
    }
}
