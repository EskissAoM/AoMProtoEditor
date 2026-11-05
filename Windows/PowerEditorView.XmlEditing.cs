using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;

namespace AoMDivineDataEditor.Windows;

public partial class PowerEditorView
{
    private sealed class PowerXmlDraft(XmlEditSession session)
    {
        internal XmlEditSession Session { get; } = session;
        internal XmlEditSession.Snapshot? Prepared { get; set; }
        internal List<OwnedStringRename> StringMigrations { get; } = [];
    }

    private readonly Dictionary<string, PowerXmlDraft> _powerXmlDrafts = new(StringComparer.OrdinalIgnoreCase);
    private XmlEditController _powerXmlController = null!;
    private PowerXmlDraft? CurrentPowerXmlDraft => IsModifiedMode && _loadedEntryName != null
        ? _powerXmlDrafts.GetValueOrDefault(_loadedEntryName) : null;
    internal bool HasXmlEditingSession => _powerXmlDrafts.Values.Any(draft => draft.Session.IsEditing);
    internal event Action<string, string>? XmlPowerRenamed;

    private static string FormatPowerXml(XElement entry)
        => XmlSyntaxEditorService.PreparePreviewText(PowerCatalog.FormatPreview(entry));

    private void InitializePowerXmlEditing()
    {
        _powerXmlController = new XmlEditController(_rawXmlEditor, _editXmlButton, _powerStructuredForm, PowerXmlChanged)
        { BeforeEditAsync = BeginPowerXmlEditingAsync };
    }

    private async Task<bool> BeginPowerXmlEditingAsync()
    {
        await _populationTask;
        if (!IsModifiedMode || _currentEntry == null || string.IsNullOrWhiteSpace(_modPath)) return false;
        if (!await CommitCurrentPowerAsync()) return false;
        await _populationTask;
        RefreshRawXmlFromCurrent();
        return IsModifiedMode && _currentEntry != null;
    }

    private void BindPowerXmlPreview(string text)
    {
        if (_currentEntry == null || _loadedEntryName == null)
        { _powerXmlController.Bind(null, false); return; }
        text = XmlSyntaxEditorService.PreparePreviewText(text);
        XmlEditSession session;
        if (IsModifiedMode)
        {
            if (!_powerXmlDrafts.TryGetValue(_loadedEntryName, out var draft))
            {
                draft = new PowerXmlDraft(new XmlEditSession(text, !string.IsNullOrWhiteSpace(_modPath)));
                _powerXmlDrafts.Add(_loadedEntryName, draft);
            }
            session = draft.Session;
            session.RefreshFromModel(text);
        }
        else session = new XmlEditSession(text, false);
        _powerXmlController.Bind(session, true);
    }

    private void PowerXmlChanged()
    {
        _rawEditorDirty = CurrentPowerXmlDraft?.Session.IsDirty == true;
        _session.MarkDirty(_loadedEntryName);
        SetDirty(true);
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private XElement ParsePowerXml(string name, PowerXmlDraft draft, string text)
    {
        var candidate = XElement.Parse(text, LoadOptions.PreserveWhitespace);
        if (candidate.Name != PowerCatalog.EntryName)
            throw new InvalidDataException("Raw XML must have one <power> root element without a namespace.");
        var newName = PowerCatalog.GetName(candidate);
        if (!InternalNamePolicy.IsValidOrUnchangedLegacy(newName, name))
            throw new InvalidDataException($"Power name can contain only {InternalNamePolicy.AllowedCharactersDescription}.");
        if (string.IsNullOrWhiteSpace(candidate.Attribute("type")?.Value) || !PowerCatalog.IsModCatalogPower(candidate))
            throw new InvalidDataException("Select a supported Power Type before saving.");
        PowerCatalog.ApplyOwnedStringRenames(candidate, draft.StringMigrations);
        return candidate;
    }

    private XDocument? CreatePowerXmlSnapshot()
    {
        if (_modDocument == null) return null;
        var document = new XDocument(_modDocument);
        var renames = _session.CreateCommitSnapshot().Renames.ToList();
        foreach (var (name, draft) in _powerXmlDrafts.Where(pair => pair.Value.Session.IsDirty))
        {
            if (draft.Prepared?.Text == draft.Session.Text) continue;
            var current = PowerCatalog.Find(document, name)
                ?? throw new InvalidDataException($"Power '{name}' is no longer available. Discard or reload its draft.");
            var candidate = ParsePowerXml(name, draft, draft.Session.Text);
            current.ReplaceWith(candidate);
            var next = PowerCatalog.GetName(candidate);
            if (!name.Equals(next, StringComparison.OrdinalIgnoreCase)) renames.Add(new(name, next));
        }
        foreach (var rename in renames)
            GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.Ability, rename.Key, rename.Value);
        var names = document.Root!.Elements(PowerCatalog.EntryName).Select(PowerCatalog.GetName).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidDataException("Power XML contains duplicate Power names.");
        return document;
    }

    internal async Task<bool> PreparePowerXmlForSaveAsync()
    {
        await _commandGate.WaitAsync();
        try { return await PreparePowerXmlCoreAsync(); }
        finally { _commandGate.Release(); }
    }

    private async Task<bool> PreparePowerXmlCoreAsync()
    {
        if (!HasXmlEditingSession) return true;
        try
        {
            // Parse/check every retained buffer before adopting any candidate.
            var proposed = CreatePowerXmlSnapshot()!;
            var catalogs = CreatePowerSaveCatalogs() with
            { PowerNames = _baseEntries.Keys.Concat(proposed.Root!.Elements(PowerCatalog.EntryName).Select(PowerCatalog.GetName)).ToList() };
            var validation = PowerSaveValidator.Validate(proposed, catalogs);
            if (!validation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
            foreach (var (name, draft) in _powerXmlDrafts.Where(pair => pair.Value.Session.IsEditing).ToArray())
            {
                var snapshot = draft.Session.CaptureForSave()!;
                if (draft.Prepared?.Text == snapshot.Text) continue;
                if (!draft.Session.IsDirty) { draft.Prepared = snapshot; continue; }
                if (!await CommitCurrentPowerCoreAsync(name, draft, snapshot)) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportSaveFeedback(EditorSaveOutcome.Blocked);
            await ShowCommandErrorAsync("Invalid Power XML", exception.Message);
            return false;
        }
    }

    private void ForgetPowerXmlDraft(string name)
    {
        if (_powerXmlDrafts.Remove(name, out var draft)) draft.Session.Reset(draft.Session.Text);
    }

    internal void ReleaseCleanPowerXmlDraft(string? name)
    {
        if (name != null && _powerXmlDrafts.GetValueOrDefault(name)?.Session.IsDirty == false) ForgetPowerXmlDraft(name);
    }

    private void ReloadPowerXmlDrafts(IEnumerable<string> names)
    {
        foreach (var name in names.ToArray())
        {
            if (!_powerXmlDrafts.TryGetValue(name, out var draft)) continue;
            if (PowerCatalog.Find(_modDocument!, name) is { } entry)
            { draft.Session.Reset(FormatPowerXml(entry)); draft.Prepared = null; draft.StringMigrations.Clear(); }
            else ForgetPowerXmlDraft(name);
        }
    }

    private void ReloadChangedCleanPowerXmlDrafts()
    {
        // An authoritative merge may replace an unchanged record without listing
        // it as a conflict. Do not leave its unlocked, untouched buffer stale.
        ReloadPowerXmlDrafts(_powerXmlDrafts.Where(pair => !pair.Value.Session.IsDirty &&
                (PowerCatalog.Find(_modDocument!, pair.Key) is not { } entry ||
                 !string.Equals(pair.Value.Session.Text, FormatPowerXml(entry), StringComparison.Ordinal)))
            .Select(pair => pair.Key).ToArray());
    }

    internal void ReleasePowerXmlEditing()
    {
        _powerXmlController.Dispose();
        foreach (var name in _powerXmlDrafts.Keys.ToArray()) ForgetPowerXmlDraft(name);
    }

    private List<(string Name, PowerXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> CapturePreparedPowerXmlSaves()
        => _powerXmlDrafts.Where(pair => pair.Value.Prepared != null)
            .Select(pair => (pair.Key, pair.Value, pair.Value.Prepared!)).ToList();

    private void AcceptPowerXmlSaves(IReadOnlyList<(string Name, PowerXmlDraft Draft, XmlEditSession.Snapshot Snapshot)> saves)
    {
        foreach (var (name, draft, snapshot) in saves)
        {
            if (!ReferenceEquals(_powerXmlDrafts.GetValueOrDefault(name), draft) ||
                PowerCatalog.Find(_modDocument!, name) is not { } entry) continue;
            if (draft.Session.AcceptSaved(snapshot, FormatPowerXml(entry)) == XmlSaveAcceptance.Saved)
                draft.StringMigrations.Clear();
            draft.Prepared = null;
            if (draft.Session.IsDirty) _session.MarkDirty(name);
        }
        _rawEditorDirty = CurrentPowerXmlDraft?.Session.IsDirty == true;
        SetDirty(_session.HasPendingWork || _powerXmlDrafts.Values.Any(draft => draft.Session.IsDirty));
        if (_currentEntry != null) BindPowerXmlPreview(PowerCatalog.FormatPreview(_currentEntry));
    }
}
